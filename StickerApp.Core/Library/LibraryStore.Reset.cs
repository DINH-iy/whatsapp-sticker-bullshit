namespace StickerApp.Core;

public sealed partial class LibraryStore
{
    /// <summary>Irreversible. UI must obtain both confirmations before invoking this.</summary>
    public async Task FactoryResetAsync()
    {
        await gate.WaitAsync();
        try
        {
            // Never recursively erase a user-selected folder. Remove only app indexes,
            // referenced library files, and recognized generated media/font files.
            foreach (var root in new[] { Root, DefaultRoot }.Distinct(StringComparer.Ordinal))
            {
                var media = Path.Combine(root, "media");
                if (Directory.Exists(media) && (File.GetAttributes(media) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("Cannot reset a library whose media directory is a symbolic link.");
            }
            foreach (var root in new[] { Root, DefaultRoot }.Distinct(StringComparer.Ordinal))
            {
                var index = Path.Combine(root, "library.json");
                var names = new HashSet<string>(StringComparer.Ordinal);
                if (File.Exists(index))
                {
                    var library = System.Text.Json.JsonSerializer.Deserialize<StickerLibrary>(await File.ReadAllTextAsync(index), Json)
                        ?? throw new InvalidDataException("The library index could not be read. No reset was completed.");
                    foreach (var name in library.Stickers.SelectMany(ImageFiles).Concat(library.Fonts.Select(font => font.FileName)))
                    {
                        if (name != Path.GetFileName(name)) throw new InvalidDataException("The library contains an invalid filename.");
                        names.Add(name);
                    }
                }
                var media = Path.Combine(root, "media");
                if (Directory.Exists(media))
                {
                    foreach (var path in Directory.EnumerateFiles(media))
                    {
                        var name = Path.GetFileName(path);
                        if (names.Contains(name) || IsGeneratedMediaFile(name) || name.EndsWith(".font", StringComparison.Ordinal) && Guid.TryParseExact(Path.GetFileNameWithoutExtension(name), "N", out _))
                            File.Delete(path);
                    }
                }
                File.Delete(index);
                File.Delete(index + ".tmp");
            }
            foreach (var file in new[] { settingsFile, ProfileFile, Path.Combine(DefaultRoot, "profile-identity.json") })
            {
                File.Delete(file); File.Delete(file + ".tmp");
            }
            Root = DefaultRoot;
            KeepOriginalStillImages = false; KeepOriginalAnimations = true;
            StorageLimitBytes = null; DecimalStorageUnits = DonutStorageChart = false;
            AskForOriginalOnImport = BlurNsfwPreviews = true;
            Directory.CreateDirectory(Path.Combine(Root, "media"));
        }
        finally { gate.Release(); }
        Changed?.Invoke();
    }
}
