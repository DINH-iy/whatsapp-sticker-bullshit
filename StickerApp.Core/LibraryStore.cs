using System.Text.Json;

namespace StickerApp.Core;

/// <summary>Single-process, atomic file persistence for the first build. Replace behind this service with SQLite later.</summary>
public sealed class LibraryStore
{
    public const int MaxImportBytes = 10 * 1024 * 1024;
    private readonly SemaphoreSlim gate = new(1, 1);
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    public string Root { get; private set; }
    public string DefaultRoot { get; }
    private readonly string settingsFile;
    private string Index => Path.Combine(Root, "library.json");
    public LibraryStore(string root)
    {
        DefaultRoot = Path.GetFullPath(root);
        // Keep the pointer in the original app directory when the library moves.
        settingsFile = Path.Combine(DefaultRoot, "storage-settings.json");
        Directory.CreateDirectory(DefaultRoot);
        var saved = File.Exists(settingsFile)
            ? JsonSerializer.Deserialize<StorageSettings>(File.ReadAllText(settingsFile)) : null;
        Root = saved is null ? DefaultRoot : Path.GetFullPath(saved.LibraryRoot);
        if (saved is not null && !Directory.Exists(Root))
            throw new DirectoryNotFoundException($"The sticker folder is unavailable: {Root}. Reconnect it before starting the app.");
        Directory.CreateDirectory(Path.Combine(Root, "media"));
    }

    private sealed record StorageSettings(string LibraryRoot);

    /// <summary>Copy to an empty folder before committing the new location. Keep the old copy as a backup.</summary>
    public async Task ChangeFolderAsync(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Path.IsPathFullyQualified(folder))
            throw new InvalidOperationException("Enter a full folder path.");
        var target = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
        await gate.WaitAsync();
        try
        {
            if (target == Path.TrimEndingDirectorySeparator(Root))
                return;
            if (IsWithin(target, Root) || IsWithin(Root, target))
                throw new InvalidOperationException("Choose a separate folder outside the current sticker folder.");
            if (Directory.Exists(target) && Directory.EnumerateFileSystemEntries(target).Any())
                throw new InvalidOperationException("Choose an empty folder so existing files are never overwritten.");
            // Validate the index before copying anything.
            await ReadInternalAsync();
            Directory.CreateDirectory(target);
            var created = new List<string>();
            try
            {
                var media = Path.Combine(target, "media");
                Directory.CreateDirectory(media);
                foreach (var source in Directory.EnumerateFiles(Path.Combine(Root, "media")))
                {
                    var destination = Path.Combine(media, Path.GetFileName(source));
                    // CreateNew also protects against files appearing during the copy.
                    await using var input = File.OpenRead(source);
                    await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write);
                    created.Add(destination);
                    await input.CopyToAsync(output);
                }
                var index = Path.Combine(target, "library.json");
                await using (var output = new FileStream(index, FileMode.CreateNew, FileAccess.Write))
                {
                    created.Add(index);
                    await JsonSerializer.SerializeAsync(output, await ReadInternalAsync(), Json);
                }
                await File.WriteAllTextAsync(settingsFile + ".tmp", JsonSerializer.Serialize(new StorageSettings(target), Json));
                File.Move(settingsFile + ".tmp", settingsFile, true);
                Root = target;
            }
            catch
            {
                // Remove only files created by this attempt; the original library stays intact.
                foreach (var file in created)
                {
                    try
                    {
                        File.Delete(file);
                    }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
                // An empty media directory would otherwise prevent retrying this destination.
                try
                {
                    Directory.Delete(Path.Combine(target, "media"), false);
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
                throw;
            }
        }
        finally
        {
            gate.Release();
        }
    }

    private static bool IsWithin(string path, string parent)
    {
        var relative = Path.GetRelativePath(parent, path);
        return relative == "." || (!Path.IsPathRooted(relative) && relative != ".." && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal));
    }

    public async Task<StickerLibrary> ReadAsync()
    {
        await gate.WaitAsync();
        try
        {
            return await ReadInternalAsync();
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<StickerLibrary> ReadInternalAsync()
    {
        if (!File.Exists(Index))
            return new();
        await using var file = File.OpenRead(Index);
        var library = await JsonSerializer.DeserializeAsync<StickerLibrary>(file)
            ?? throw new InvalidDataException("The library is empty or damaged.");
        if (library.SchemaVersion != 1)
            throw new InvalidDataException("This library needs a newer version of the app.");
        return library;
    }

    private async Task MutateAsync(Action<StickerLibrary> action)
    {
        await gate.WaitAsync();
        try
        {
            await MutateInternalAsync(action);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task MutateInternalAsync(Action<StickerLibrary> action)
    {
        var state = await ReadInternalAsync();
        action(state);
        var temp = Index + ".tmp";
        await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(state, Json));
        File.Move(temp, Index, true);
    }

    public async Task ImportAsync(string name, byte[] bytes, Guid? packId = null)
    {
        await gate.WaitAsync();
        try
        {
            if (bytes.Length is 0 or > MaxImportBytes)
                throw new InvalidDataException("Choose a file smaller than 10 MB.");
            var info = MediaInspector.Inspect(bytes);
            var s = new Sticker
            {
                Name = CleanName(Path.GetFileNameWithoutExtension(name)),
                MimeType = info.MimeType,
                Width = info.Width,
                Height = info.Height,
                Animated = info.Animated,
                Bytes = bytes.Length,
                DurationMs = info.DurationMs,
                MinimumFrameMs = info.MinimumFrameMs
            };
            s.FileName = s.Id.ToString("N") + info.Extension;
            var path = MediaPath(s.FileName);
            await File.WriteAllBytesAsync(path, bytes);
            try
            {
                // Add the image and its pack membership in the same index write.
                // Rejected images are removed from disk by the catch below.
                await MutateInternalAsync(l =>
                {
                    if (packId is { } destinationId)
                    {
                        var pack = l.Packs.FirstOrDefault(p => p.Id == destinationId)
                            ?? throw new InvalidOperationException("This pack no longer exists.");
                        if (pack.StickerIds.Count >= 30)
                            throw new InvalidOperationException("A pack can contain at most 30 stickers.");
                        if (pack.Animated != s.Animated)
                            throw new InvalidOperationException($"Choose {(pack.Animated ? "animated images" : "still images")} for this pack.");
                        pack.StickerIds.Add(s.Id);
                        Touch(pack);
                    }
                    l.Stickers.Add(s);
                });
            }
            catch
            {
                File.Delete(path);
                throw;
            }
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<Guid> CreatePackAsync(string name, bool animated)
    {
        var pack = new StickerPack { Name = CleanName(name), Animated = animated };
        await MutateAsync(l =>
        {
            if (l.Packs.Count >= 10)
                throw new InvalidOperationException("This build supports up to 10 packs.");
            l.Packs.Add(pack);
        });
        return pack.Id;
    }

    public Task SetPackCoverAsync(Guid id, byte[]? bytes)
    {
        var cover = bytes is null ? null : PackCover.FromBytes(bytes);
        return MutateAsync(library =>
        {
            var pack = library.Packs.FirstOrDefault(p => p.Id == id)
                ?? throw new InvalidOperationException("This pack no longer exists.");
            pack.CoverImage = cover;
            Touch(pack);
        });
    }

    // Creating the pack and adding its first image share one atomic write.
    public Task CreatePackWithStickerAsync(string name, Guid stickerId) => MutateAsync(library =>
    {
        if (library.Packs.Count >= 10)
            throw new InvalidOperationException("This build supports up to 10 packs.");

        var sticker = library.Stickers.FirstOrDefault(image => image.Id == stickerId)
            ?? throw new InvalidOperationException("This image is no longer in the library.");

        library.Packs.Add(new StickerPack
        {
            Name = CleanName(name),
            Animated = sticker.Animated,
            StickerIds = [sticker.Id]
        });
    });

    public Task RenamePackAsync(Guid id, string name) => MutateAsync(l => { var p = l.Packs.Single(p => p.Id == id); p.Name = CleanName(name); Touch(p); });
    public Task DeletePackAsync(Guid id) => MutateAsync(l => l.Packs.RemoveAll(p => p.Id == id));
    public Task FavoriteAsync(Guid id) => MutateAsync(l => { var s = l.Stickers.Single(s => s.Id == id); s.Favorite = !s.Favorite; });
    public Task EditStickerAsync(Guid id, string name, string tags) => MutateAsync(l =>
    {
        var s = l.Stickers.Single(s => s.Id == id);
        s.Name = CleanName(name);
        s.Tags = tags.Trim()[..Math.Min(tags.Trim().Length, 256)];
        foreach (var p in l.Packs.Where(p => p.StickerIds.Contains(id)))
            Touch(p);
    });

    public async Task ReplaceImageAsync(Guid id, byte[] bytes)
    {
        await gate.WaitAsync();
        try
        {
            if (bytes.Length is 0 or > MaxImportBytes)
                throw new InvalidDataException("Maximum file size: 10 MB.");
            var info = MediaInspector.Inspect(bytes);
            var fileName = Guid.NewGuid().ToString("N") + info.Extension;
            var path = MediaPath(fileName);
            await File.WriteAllBytesAsync(path, bytes);
            try
            {
                await MutateInternalAsync(l =>
                {
                    var sticker = l.Stickers.Single(s => s.Id == id);
                    if (l.Packs.Any(p => p.StickerIds.Contains(id) && p.Animated != info.Animated))
                        throw new InvalidOperationException("Remove the image from its pack before changing between static and animated.");
                    sticker.OriginalFileName ??= sticker.FileName;
                    sticker.FileName = fileName;
                    sticker.MimeType = info.MimeType;
                    sticker.Width = info.Width;
                    sticker.Height = info.Height;
                    sticker.Bytes = bytes.Length;
                    sticker.Animated = info.Animated;
                    sticker.DurationMs = info.DurationMs;
                    sticker.MinimumFrameMs = info.MinimumFrameMs;
                    foreach (var pack in l.Packs.Where(p => p.StickerIds.Contains(id)))
                        Touch(pack);
                });
            }
            catch
            {
                File.Delete(path);
                throw;
            }
        }
        finally
        {
            gate.Release();
        }
    }

    public Task AddToPackAsync(Guid packId, IEnumerable<Guid> ids) => MutateAsync(l =>
    {
        var p = l.Packs.Single(p => p.Id == packId);
        var additions = ids.Distinct().Where(id => !p.StickerIds.Contains(id)).ToList();
        if (p.StickerIds.Count + additions.Count > 30)
            throw new InvalidOperationException("A pack can contain at most 30 stickers.");
        foreach (var id in additions)
            if (l.Stickers.Single(s => s.Id == id).Animated != p.Animated)
                throw new InvalidOperationException("Use separate packs for static and animated stickers.");
        p.StickerIds.AddRange(additions);
        Touch(p);
    });

    public Task RemoveFromPackAsync(Guid packId, IEnumerable<Guid> ids) => MutateAsync(l =>
    {
        var p = l.Packs.Single(p => p.Id == packId);
        var remove = ids.ToHashSet();
        p.StickerIds.RemoveAll(remove.Contains);
        Touch(p);
    });

    public Task ReorderAsync(Guid packId, Guid stickerId, int direction) => MutateAsync(l =>
    {
        var p = l.Packs.Single(p => p.Id == packId);
        var i = p.StickerIds.IndexOf(stickerId);
        var j = i + Math.Sign(direction);
        if (i >= 0 && j >= 0 && j < p.StickerIds.Count)
        {
            (p.StickerIds[i], p.StickerIds[j]) = (p.StickerIds[j], p.StickerIds[i]);
            Touch(p);
        }
    });

    public async Task DeleteStickersAsync(IEnumerable<Guid> ids)
    {
        await gate.WaitAsync();
        try
        {
            var remove = ids.ToHashSet();
            List<string> files = [];
            await MutateInternalAsync(l =>
            {
                files = l.Stickers.Where(s => remove.Contains(s.Id)).SelectMany(s => new[] { s.FileName, s.OriginalFileName }).OfType<string>().Distinct().ToList();
                l.Stickers.RemoveAll(s => remove.Contains(s.Id));
                foreach (var p in l.Packs.Where(p => p.StickerIds.Any(remove.Contains)))
                {
                    p.StickerIds.RemoveAll(remove.Contains);
                    Touch(p);
                }
            });
            foreach (var file in files)
                File.Delete(MediaPath(file));
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<PackArchive.ExportInfo> ExportPackAsync(Guid packId, Stream destination)
    {
        await gate.WaitAsync();
        try
        {
            var library = await ReadInternalAsync();
            var pack = library.Packs.FirstOrDefault(candidate => candidate.Id == packId)
                ?? throw new InvalidOperationException("This pack no longer exists.");
            var images = pack.StickerIds.Select(id => library.Stickers.FirstOrDefault(image => image.Id == id)
                ?? throw new InvalidDataException("A pack image is missing from the library.")).ToList();
            return await PackArchive.WriteAsync(destination, pack, images, image => File.OpenRead(MediaPath(image.FileName)));
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<Guid> ImportPackAsync(Stream source)
    {
        // Validate everything before touching the library. No archive entry is extracted by its supplied path.
        var imported = await PackArchive.ReadAsync(source);
        await gate.WaitAsync();
        var createdFiles = new List<string>();
        try
        {
            var library = await ReadInternalAsync();
            if (library.Packs.Count >= 10)
                throw new InvalidOperationException("This build supports up to 10 packs.");

            var pack = new StickerPack { Name = imported.Name, Animated = imported.Animated, CoverImage = imported.CoverImage };
            var images = new List<Sticker>();
            foreach (var image in imported.Images)
            {
                var sticker = new Sticker
                {
                    Name = image.Name,
                    Tags = image.Tags,
                    MimeType = image.Info.MimeType,
                    Width = image.Info.Width,
                    Height = image.Info.Height,
                    Animated = image.Info.Animated,
                    Bytes = image.Bytes.Length,
                    DurationMs = image.Info.DurationMs,
                    MinimumFrameMs = image.Info.MinimumFrameMs
                };
                sticker.FileName = $"{sticker.Id:N}{image.Info.Extension}";
                var path = MediaPath(sticker.FileName);
                await using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write))
                {
                    createdFiles.Add(path);
                    await output.WriteAsync(image.Bytes);
                }
                images.Add(sticker);
                pack.StickerIds.Add(sticker.Id);
            }

            await MutateInternalAsync(state =>
            {
                state.Stickers.AddRange(images);
                state.Packs.Add(pack);
            });
            return pack.Id;
        }
        catch
        {
            foreach (var file in createdFiles)
            {
                try { File.Delete(file); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
            throw;
        }
        finally
        {
            gate.Release();
        }
    }

    public string MediaPath(string fileName)
    {
        if (fileName != Path.GetFileName(fileName) || string.IsNullOrEmpty(fileName))
            throw new InvalidDataException("Invalid media filename.");
        return Path.Combine(Root, "media", fileName);
    }
    public async Task<string> PreviewAsync(Sticker sticker)
    {
        await gate.WaitAsync();
        try
        {
            return $"data:{sticker.MimeType};base64,{Convert.ToBase64String(await File.ReadAllBytesAsync(MediaPath(sticker.FileName)))}";
        }
        finally
        {
            gate.Release();
        }
    }
    private static void Touch(StickerPack p) => p.Version = Guid.NewGuid().ToString("N");
    private static string CleanName(string name) => !string.IsNullOrWhiteSpace(name) && name.Trim().Length <= 128
        ? name.Trim() : throw new InvalidOperationException("Use a name of 1–128 characters.");
}
