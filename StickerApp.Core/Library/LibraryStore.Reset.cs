namespace StickerApp.Core;

public sealed partial class LibraryStore
{
    /// <summary>Irreversible. UI must obtain both confirmations before invoking this.</summary>
    public async Task FactoryResetAsync(IProgress<FactoryResetProgress>? progress = null)
    {
        progress?.Report(new("Waiting for library operations…"));
        await gate.WaitAsync();
        try
        {
            // File deletion can be slow on phones. Keep disk work off the UI thread.
            await Task.Run(async () =>
            {
                progress?.Report(new("Checking app files…"));
                var files = new HashSet<string>(StringComparer.Ordinal);
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
                                files.Add(path);
                        }
                    }
                    files.Add(index);
                    files.Add(index + ".tmp");
                }
                foreach (var file in new[] { settingsFile, MediaSettingsFile, ProfileFile, Path.Combine(DefaultRoot, "profile-identity.json") })
                {
                    files.Add(file); files.Add(file + ".tmp");
                }
                // Finish validating both locations before deleting anything.
                var pending = files.Where(File.Exists).ToList();
                var completed = 0;
                var lastUpdate = System.Diagnostics.Stopwatch.StartNew();
                progress?.Report(new("Deleting app files…", 0, pending.Count));
                foreach (var file in pending)
                {
                    File.Delete(file);
                    completed++;
                    if (completed == 1 || completed == pending.Count || lastUpdate.ElapsedMilliseconds >= 50)
                    {
                        progress?.Report(new("Deleting app files…", completed, pending.Count));
                        lastUpdate.Restart();
                    }
                }
                progress?.Report(new("Restoring app defaults…", completed, pending.Count));
                Root = DefaultRoot;
                GifImportLimitBytes = MaxGifImportBytes;
                KeepOriginalStillImages = false; KeepOriginalAnimations = true;
                StorageLimitBytes = null; DecimalStorageUnits = DonutStorageChart = false;
                AskForOriginalOnImport = BlurNsfwPreviews = true;
                Directory.CreateDirectory(Path.Combine(Root, "media"));
            });
        }
        finally { gate.Release(); }
        Changed?.Invoke();
    }
}
