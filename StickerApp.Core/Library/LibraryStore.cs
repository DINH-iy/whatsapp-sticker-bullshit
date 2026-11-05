using System.Text.Json;

namespace StickerApp.Core;

public sealed record BatchImportFile(string Name, string Path, bool? KeepOriginal = null);

/// <summary>Single-process, atomic file persistence for the first build. Replace behind this service with SQLite later.</summary>
public sealed partial class LibraryStore
{
    public const int MaxImportBytes = 10 * 1024 * 1024;
    private readonly SemaphoreSlim gate = new(1, 1);
    public event Action? Changed;
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    public string Root { get; private set; }
    public string DefaultRoot { get; }
    public bool KeepOriginalStillImages { get; private set; }
    public bool KeepOriginalAnimations { get; private set; } = true;
    public long? StorageLimitBytes { get; private set; }
    public bool DecimalStorageUnits { get; private set; }
    public bool DonutStorageChart { get; private set; }
    public bool AskForOriginalOnImport { get; private set; } = true;
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
        KeepOriginalStillImages = saved?.KeepOriginalStillImages ?? false;
        KeepOriginalAnimations = saved?.KeepOriginalAnimations ?? true;
        StorageLimitBytes = saved?.StorageLimitBytes;
        DecimalStorageUnits = saved?.DecimalStorageUnits ?? false;
        DonutStorageChart = saved?.DonutStorageChart ?? false;
        AskForOriginalOnImport = saved?.AskForOriginalOnImport ?? true;
        if (saved is not null && !Directory.Exists(Root))
            throw new DirectoryNotFoundException($"The sticker folder is unavailable: {Root}. Reconnect it before starting the app.");
        Directory.CreateDirectory(Path.Combine(Root, "media"));
    }

    private sealed record StorageSettings(string LibraryRoot, bool KeepOriginalStillImages = false,
        bool KeepOriginalAnimations = true, long? StorageLimitBytes = null, bool DecimalStorageUnits = false,
        bool DonutStorageChart = false, bool AskForOriginalOnImport = true);

    public async Task SetAskForOriginalOnImportAsync(bool ask)
    {
        await gate.WaitAsync();
        try
        {
            await File.WriteAllTextAsync(settingsFile + ".tmp", JsonSerializer.Serialize(
                new StorageSettings(Root, KeepOriginalStillImages, KeepOriginalAnimations, StorageLimitBytes, DecimalStorageUnits, DonutStorageChart, ask), Json));
            File.Move(settingsFile + ".tmp", settingsFile, true);
            AskForOriginalOnImport = ask;
        }
        finally { gate.Release(); }
    }

    public async Task SetDonutStorageChartAsync(bool useDonut)
    {
        await gate.WaitAsync();
        try
        {
            await File.WriteAllTextAsync(settingsFile + ".tmp", JsonSerializer.Serialize(
                new StorageSettings(Root, KeepOriginalStillImages, KeepOriginalAnimations, StorageLimitBytes, DecimalStorageUnits, useDonut, AskForOriginalOnImport), Json));
            File.Move(settingsFile + ".tmp", settingsFile, true);
            DonutStorageChart = useDonut;
        }
        finally { gate.Release(); }
    }

    public async Task SetDecimalStorageUnitsAsync(bool useDecimal)
    {
        await gate.WaitAsync();
        try
        {
            await File.WriteAllTextAsync(settingsFile + ".tmp", JsonSerializer.Serialize(
                new StorageSettings(Root, KeepOriginalStillImages, KeepOriginalAnimations, StorageLimitBytes, useDecimal, DonutStorageChart, AskForOriginalOnImport), Json));
            File.Move(settingsFile + ".tmp", settingsFile, true);
            DecimalStorageUnits = useDecimal;
        }
        finally { gate.Release(); }
    }

    public async Task SetOriginalRetentionAsync(bool keepStills, bool keepAnimations)
    {
        await gate.WaitAsync();
        try
        {
            await File.WriteAllTextAsync(settingsFile + ".tmp", JsonSerializer.Serialize(new StorageSettings(Root, keepStills, keepAnimations, StorageLimitBytes, DecimalStorageUnits, DonutStorageChart, AskForOriginalOnImport), Json));
            File.Move(settingsFile + ".tmp", settingsFile, true);
            KeepOriginalStillImages = keepStills;
            KeepOriginalAnimations = keepAnimations;
        }
        finally { gate.Release(); }
    }

    public async Task SetStorageLimitAsync(long? limitBytes)
    {
        if (limitBytes.HasValue && (limitBytes < 1024 * 1024 || limitBytes > 1024L * 1024 * 1024 * 1024))
            throw new InvalidOperationException("Choose a library limit between 1 MiB and 1 TiB.");
        await gate.WaitAsync();
        try
        {
            await File.WriteAllTextAsync(settingsFile + ".tmp", JsonSerializer.Serialize(
                new StorageSettings(Root, KeepOriginalStillImages, KeepOriginalAnimations, limitBytes, DecimalStorageUnits, DonutStorageChart, AskForOriginalOnImport), Json));
            File.Move(settingsFile + ".tmp", settingsFile, true);
            StorageLimitBytes = limitBytes;
        }
        finally { gate.Release(); }
    }

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
                await File.WriteAllTextAsync(settingsFile + ".tmp", JsonSerializer.Serialize(new StorageSettings(target, KeepOriginalStillImages, KeepOriginalAnimations, StorageLimitBytes, DecimalStorageUnits, DonutStorageChart, AskForOriginalOnImport), Json));
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
        await SaveLibrarySnapshotAsync(state);
    }

    public async Task ImportAsync(string name, byte[] bytes, Guid? packId = null, bool? keepOriginal = null)
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
                KeepOriginalOverride = keepOriginal ?? (info.Animated ? KeepOriginalAnimations : KeepOriginalStillImages),
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

    /// <summary>Validate and commit the whole selection together, or remove every new media file.</summary>
    public async Task<Guid?> ImportBatchAsync(IReadOnlyList<BatchImportFile> files, Guid? packId = null,
        CancellationToken cancellationToken = default, string? newPackName = null, string? newPackPublisher = null)
    {
        if (files.Count is < 1 or > PinterestImportClient.MaxImages)
            throw new InvalidDataException("Choose between 1 and 100 images.");
        await gate.WaitAsync(cancellationToken);
        List<string> created = [];
        try
        {
            var library = await ReadInternalAsync();
            StickerPack? newPack = null;
            if (newPackName is not null)
            {
                if (packId is not null)
                    throw new InvalidOperationException("Choose an existing pack or create a new one.");
                if (library.Packs.Count >= 10)
                    throw new InvalidOperationException("This build supports up to 10 packs.");
                if (files.Count > 30)
                    throw new InvalidOperationException("A pack can contain at most 30 stickers. Select fewer images or choose the library.");
                newPack = new StickerPack { Name = CleanName(newPackName), Publisher = CleanPublisher(newPackPublisher ?? "Sticker Studio") };
            }
            if (packId is { } destination)
            {
                var pack = library.Packs.FirstOrDefault(p => p.Id == destination)
                    ?? throw new InvalidOperationException("This pack no longer exists.");
                if (pack.StickerIds.Count + files.Count > 30)
                    throw new InvalidOperationException($"This pack has room for {30 - pack.StickerIds.Count} more stickers. Select fewer images or choose the library.");
            }
            List<Sticker> stickers = [];
            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await using var input = File.OpenRead(file.Path);
                if (input.Length is 0 or > MaxImportBytes)
                    throw new InvalidDataException("Choose images smaller than 10 MB.");
                var bytes = new byte[(int)input.Length];
                await input.ReadExactlyAsync(bytes, cancellationToken);
                var info = MediaInspector.Inspect(bytes);
                var sticker = new Sticker
                {
                    Name = CleanName(file.Name), MimeType = info.MimeType,
                    KeepOriginalOverride = file.KeepOriginal ?? (info.Animated ? KeepOriginalAnimations : KeepOriginalStillImages),
                    Width = info.Width, Height = info.Height, Animated = info.Animated,
                    Bytes = bytes.Length, DurationMs = info.DurationMs, MinimumFrameMs = info.MinimumFrameMs
                };
                sticker.FileName = $"{sticker.Id:N}{info.Extension}";
                var path = MediaPath(sticker.FileName);
                await using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write))
                {
                    created.Add(path);
                    await output.WriteAsync(bytes, cancellationToken);
                }
                stickers.Add(sticker);
            }
            cancellationToken.ThrowIfCancellationRequested();
            // Once the atomic index write starts it must finish, even if the caller cancels.
            await MutateInternalAsync(state =>
            {
                state.Stickers.AddRange(stickers);
                if (newPack is not null)
                {
                    newPack.StickerIds.AddRange(stickers.Select(s => s.Id));
                    state.Packs.Add(newPack);
                }
                else if (packId is { } id)
                {
                    var pack = state.Packs.Single(p => p.Id == id);
                    pack.StickerIds.AddRange(stickers.Select(s => s.Id));
                    Touch(pack);
                }
            });
            return newPack?.Id ?? packId;
        }
        catch
        {
            foreach (var path in created)
            {
                try { File.Delete(path); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
            throw;
        }
        finally { gate.Release(); }
    }

    public async Task<Guid> CreatePackAsync(string name, string publisher = "Sticker Studio")
    {
        var pack = new StickerPack { Name = CleanName(name), Publisher = CleanPublisher(publisher) };
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
    public Task CreatePackWithStickerAsync(string name, Guid stickerId, string publisher = "Sticker Studio") => MutateAsync(library =>
    {
        if (library.Packs.Count >= 10)
            throw new InvalidOperationException("This build supports up to 10 packs.");

        var sticker = library.Stickers.FirstOrDefault(image => image.Id == stickerId)
            ?? throw new InvalidOperationException("This image is no longer in the library.");

        library.Packs.Add(new StickerPack
        {
            Name = CleanName(name),
            Publisher = CleanPublisher(publisher),
            Animated = sticker.Animated,
            StickerIds = [sticker.Id]
        });
    });

    public Task RenamePackAsync(Guid id, string name) => MutateAsync(l => { var p = l.Packs.Single(p => p.Id == id); p.Name = CleanName(name); Touch(p); });
    public Task UpdatePackDetailsAsync(Guid id, string name, string publisher) => MutateAsync(library =>
    {
        var pack = library.Packs.FirstOrDefault(pack => pack.Id == id)
            ?? throw new InvalidOperationException("This pack no longer exists.");
        pack.Name = CleanName(name);
        pack.Publisher = CleanPublisher(publisher);
        Touch(pack);
    });
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

    public async Task<bool> ReplaceImageAsync(Guid id, byte[] bytes, string? expectedFileName = null, CancellationToken cancellationToken = default,
        bool? keepOriginal = null)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (expectedFileName is not null)
            {
                var current = (await ReadInternalAsync()).Stickers.FirstOrDefault(image => image.Id == id);
                if (current is null || current.FileName != expectedFileName) return false;
            }
            if (bytes.Length is 0 or > MaxImportBytes)
                throw new InvalidDataException("Maximum file size: 10 MB.");
            var info = MediaInspector.Inspect(bytes);
            var fileName = Guid.NewGuid().ToString("N") + info.Extension;
            var path = MediaPath(fileName);
            var obsoleteFiles = new List<string>();
            var retainedFiles = new HashSet<string>();
            try
            {
                await File.WriteAllBytesAsync(path, bytes, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                await MutateInternalAsync(l =>
                {
                    var sticker = l.Stickers.Single(s => s.Id == id);
                    obsoleteFiles = ImageFiles(sticker).Where(file => file != sticker.CompositionFileName).ToList();
                    if (keepOriginal.HasValue) sticker.KeepOriginalOverride = keepOriginal;
                    var retainOriginal = sticker.KeepOriginalOverride ??
                        (sticker.Animated || info.Animated ? KeepOriginalAnimations : KeepOriginalStillImages);
                    var prepared = info.MimeType == "image/webp" && info.Width == 512 && info.Height == 512 &&
                        bytes.Length <= (info.Animated ? 500 : 100) * 1024 &&
                        (!info.Animated || (info.DurationMs is > 0 and <= 10_000 && info.MinimumFrameMs >= 8));
                    // Retain a recovery copy until preparation succeeds, even when the user opts out.
                    sticker.OriginalFileName = retainOriginal || !prepared ? sticker.OriginalFileName ?? sticker.FileName : null;
                    sticker.OriginalDiscarded |= !retainOriginal && prepared;
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
                    retainedFiles = l.Stickers.SelectMany(ImageFiles).ToHashSet();
                });
            }
            catch
            {
                File.Delete(path);
                throw;
            }
            // Cleanup cannot roll back a committed image if a file could not be deleted.
            foreach (var file in obsoleteFiles.Distinct().Except(retainedFiles)) TryDeleteMedia(file);
            return true;
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
            if (!l.Stickers.Any(s => s.Id == id))
                throw new InvalidOperationException("This image is no longer in the library.");
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
                files = l.Stickers.Where(s => remove.Contains(s.Id)).SelectMany(s => new[] { s.FileName, s.OriginalFileName, s.CompositionFileName }).OfType<string>().Distinct().ToList();
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

    public async Task<PackArchive.ExportInfo> ExportPackAsync(Guid packId, Stream destination, byte[]? whatsAppCover = null)
    {
        await gate.WaitAsync();
        try
        {
            var library = await ReadInternalAsync();
            var pack = library.Packs.FirstOrDefault(candidate => candidate.Id == packId)
                ?? throw new InvalidOperationException("This pack no longer exists.");
            var images = pack.StickerIds.Select(id => library.Stickers.FirstOrDefault(image => image.Id == id)
                ?? throw new InvalidDataException("A pack image is missing from the library.")).ToList();
            return whatsAppCover is null
                ? await PackArchive.WriteAsync(destination, pack, images, image => File.OpenRead(MediaPath(image.FileName)))
                : await WhatsAppArchive.WriteAsync(destination, pack, images, image => File.OpenRead(MediaPath(image.FileName)), whatsAppCover);
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

            var pack = new StickerPack { Name = imported.Name, Animated = imported.Animated, CoverImage = imported.CoverImage, Publisher = imported.Publisher };
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
                    KeepOriginalOverride = image.Info.Animated ? KeepOriginalAnimations : KeepOriginalStillImages,
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
            var bytes = await File.ReadAllBytesAsync(MediaPath(sticker.FileName));
            if (sticker.Animated && sticker.MimeType == "image/webp") bytes = MediaInspector.ForLoopingPreview(bytes);
            return $"data:{sticker.MimeType};base64,{Convert.ToBase64String(bytes)}";
        }
        finally
        {
            gate.Release();
        }
    }
    private static void Touch(StickerPack p) => p.Version = Guid.NewGuid().ToString("N");
    private static string CleanName(string name) => !string.IsNullOrWhiteSpace(name) && name.Trim().Length <= 128
        ? name.Trim() : throw new InvalidOperationException("Use a name of 1–128 characters.");
    private static string CleanPublisher(string publisher) => !string.IsNullOrWhiteSpace(publisher) &&
        publisher.Trim().Length <= 128 && !publisher.Any(char.IsControl)
        ? publisher.Trim() : throw new InvalidOperationException("Use a publisher of 1–128 characters on a single line.");
}
