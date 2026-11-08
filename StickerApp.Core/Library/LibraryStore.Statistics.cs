using System.Security.Cryptography;

namespace StickerApp.Core;

public sealed record LibraryStatistics(int Images, int AnimatedImages, int Packs, long CurrentBytes,
    long OriginalBytes, long LayerBytes, long UnusedBytes, int UnusedFiles, long IndexBytes,
    long MediaBytes, string Root, long RemovableOriginalBytes, int RemovableOriginals,
    IReadOnlyList<PackStorageStatistics> PackStorage, IReadOnlyList<LibraryStorageSegment> StorageSegments)
{
    public long FontBytes { get; init; }
    public IReadOnlyList<FontStorageStatistics> Fonts { get; init; } = [];
}
public sealed record FontStorageStatistics(string Name, long Bytes);
public sealed record PackStorageStatistics(Guid Id, string Name, int Images, long StickerBytes, long SourceBytes);
public sealed record LibraryStorageSegment(string Label, long Bytes, string ColorClass);
public sealed record DuplicateImage(Guid Id, string Name, long Bytes, bool Animated);
public sealed record DuplicateGroup(string Fingerprint, IReadOnlyList<DuplicateImage> Images);
public sealed record LibraryScan(LibraryStatistics Statistics, IReadOnlyList<DuplicateGroup> Duplicates,
    IReadOnlyList<string> UnusedFiles, IReadOnlyList<string> OriginalFiles);
public sealed record DuplicateSelection(string Fingerprint, Guid KeepId, IReadOnlyList<Guid> RemoveIds);

public sealed partial class LibraryStore
{
    public async Task<LibraryStatistics> GetStatisticsAsync()
    {
        await gate.WaitAsync();
        try { return MeasureLibrary(await ReadInternalAsync()); }
        finally { gate.Release(); }
    }

    public async Task<LibraryScan> ScanLibraryAsync()
    {
        await gate.WaitAsync();
        try
        {
            var library = await ReadInternalAsync();
            var groups = new Dictionary<string, List<DuplicateImage>>();
            foreach (var image in library.Stickers)
            {
                var fingerprint = await ImageFingerprintAsync(image);
                if (!groups.TryGetValue(fingerprint, out var group)) groups[fingerprint] = group = [];
                group.Add(new(image.Id, image.Name, new FileInfo(MediaPath(image.FileName)).Length, image.Animated));
            }
            return new(MeasureLibrary(library), groups.Where(pair => pair.Value.Count > 1)
                .Select(pair => new DuplicateGroup(pair.Key, pair.Value)).ToList(), FindUnusedFiles(library),
                library.Stickers.Select(image => image.OriginalFileName).OfType<string>().Distinct().ToList());
        }
        finally { gate.Release(); }
    }

    // Revalidate the reviewed content under the same lock as the write. Never merge images whose
    // originals or editable sources differ, even when their flattened previews look identical.
    public async Task<int> RemoveDuplicatesAsync(IReadOnlyList<DuplicateSelection> selections)
    {
        await gate.WaitAsync();
        try
        {
            var library = await ReadInternalAsync();
            var involved = new HashSet<Guid>();
            var removals = new HashSet<Guid>();
            foreach (var selection in selections)
            {
                if (selection.RemoveIds.Count == 0) continue;
                var ids = selection.RemoveIds.Prepend(selection.KeepId).ToList();
                if (ids.Any(id => !involved.Add(id)))
                    throw new InvalidOperationException("Duplicate selections overlap. Scan again before removing copies.");
                var images = ids.Select(id => library.Stickers.FirstOrDefault(image => image.Id == id)
                    ?? throw new InvalidOperationException("The library changed. Scan again before removing copies.")).ToList();
                foreach (var image in images)
                    if (await ImageFingerprintAsync(image) != selection.Fingerprint)
                        throw new InvalidOperationException("An image changed. Scan again before removing copies.");
                var keeper = images[0];
                var tags = string.Join("; ", images.Select(image => image.Tags).Where(tag => !string.IsNullOrWhiteSpace(tag)).Distinct());
                if (tags.Length > 256)
                    throw new InvalidOperationException("These copies have too many combined tags. Shorten their tags before merging.");
                keeper.Tags = tags;
                keeper.Favorite = images.Any(image => image.Favorite);
                keeper.Nsfw = images.Any(image => image.Nsfw);
                var groupRemovals = selection.RemoveIds.ToHashSet();
                foreach (var pack in library.Packs.Where(pack => pack.StickerIds.Any(groupRemovals.Contains)))
                {
                    pack.StickerIds = pack.StickerIds.Select(id => groupRemovals.Contains(id) ? keeper.Id : id).Distinct().ToList();
                    Touch(pack);
                }
                removals.UnionWith(groupRemovals);
            }
            if (removals.Count == 0) return 0;
            var removedFiles = library.Stickers.Where(image => removals.Contains(image.Id)).SelectMany(ImageFiles).ToHashSet();
            library.Stickers.RemoveAll(image => removals.Contains(image.Id));
            await SaveLibrarySnapshotAsync(library);
            var retainedFiles = library.Stickers.SelectMany(ImageFiles).Concat(library.Fonts.Select(f => f.FileName)).ToHashSet();
            foreach (var file in removedFiles.Except(retainedFiles)) TryDeleteMedia(file);
            return removals.Count;
        }
        finally { gate.Release(); }
    }

    public async Task<long> RemoveUnusedFilesAsync(string expectedRoot, IReadOnlyList<string> reviewedFiles)
    {
        await gate.WaitAsync();
        try
        {
            if (Root != expectedRoot)
                throw new InvalidOperationException("The storage folder changed. Scan again before cleaning up.");
            var unused = FindUnusedFiles(await ReadInternalAsync()).ToHashSet();
            long removedBytes = 0;
            foreach (var file in reviewedFiles.Distinct().Where(unused.Contains))
            {
                var path = MediaPath(file);
                var size = new FileInfo(path).Length;
                if (TryDeleteMedia(file)) removedBytes += size;
            }
            return removedBytes;
        }
        finally { gate.Release(); }
    }

    public async Task<long> RemoveOriginalsAsync(string expectedRoot, IReadOnlyList<string> reviewedFiles)
    {
        await gate.WaitAsync();
        try
        {
            if (Root != expectedRoot)
                throw new InvalidOperationException("The storage folder changed. Refresh before removing originals.");
            var library = await ReadInternalAsync();
            var reviewed = reviewedFiles.ToHashSet();
            var affected = library.Stickers.Where(image => IsPreparedSticker(image) && image.OriginalFileName is not null &&
                reviewed.Contains(image.OriginalFileName)).ToList();
            var files = affected.Select(image => image.OriginalFileName!).Distinct().ToList();
            foreach (var image in affected)
            {
                image.OriginalFileName = null;
                image.OriginalDiscarded = true;
                image.KeepOriginalOverride = false;
            }
            if (affected.Count == 0) return 0;
            await SaveLibrarySnapshotAsync(library);
            var retained = library.Stickers.SelectMany(ImageFiles).Concat(library.Fonts.Select(f => f.FileName)).ToHashSet();
            long freed = 0;
            foreach (var file in files.Except(retained))
            {
                var path = MediaPath(file);
                if (!File.Exists(path)) continue;
                var size = new FileInfo(path).Length;
                if (TryDeleteMedia(file)) freed += size;
            }
            return freed;
        }
        finally { gate.Release(); }
    }

    private LibraryStatistics MeasureLibrary(StickerLibrary library)
    {
        var files = Directory.EnumerateFiles(Path.Combine(Root, "media")).Select(path => new FileInfo(path))
            .ToDictionary(file => file.Name, file => file.Length);
        var current = library.Stickers.Select(image => image.FileName).ToHashSet();
        var originals = library.Stickers.Select(image => image.OriginalFileName).OfType<string>().Except(current).ToHashSet();
        var layers = library.Stickers.Select(image => image.CompositionFileName).OfType<string>().Except(current).Except(originals).ToHashSet();
        var unused = FindUnusedFiles(library);
        var protectedFiles = library.Stickers.SelectMany(image => !IsPreparedSticker(image) ? ImageFiles(image) :
            new[] { image.FileName, image.CompositionFileName }.OfType<string>()).ToHashSet();
        var removable = library.Stickers.Where(IsPreparedSticker).Select(image => image.OriginalFileName)
            .OfType<string>().Distinct().Except(protectedFiles).ToList();
        long Size(IEnumerable<string> names) => names.Sum(name => files.GetValueOrDefault(name));
        var packs = library.Packs.Select(pack =>
        {
            var images = library.Stickers.Where(image => pack.StickerIds.Contains(image.Id)).ToList();
            var stickers = images.Select(image => image.FileName).ToHashSet();
            return new PackStorageStatistics(pack.Id, pack.Name, images.Count, Size(stickers),
                Size(images.SelectMany(ImageFiles).Distinct().Except(stickers)));
        }).ToList();
        var owners = new Dictionary<string, HashSet<int>>();
        for (var packIndex = 0; packIndex < library.Packs.Count; packIndex++)
        {
            var pack = library.Packs[packIndex];
            foreach (var file in library.Stickers.Where(image => pack.StickerIds.Contains(image.Id)).SelectMany(ImageFiles).Distinct())
            {
                if (!owners.TryGetValue(file, out var memberships)) owners[file] = memberships = [];
                memberships.Add(packIndex);
            }
        }
        var referenced = library.Stickers.SelectMany(ImageFiles).Concat(library.Fonts.Select(f => f.FileName)).ToHashSet();
        var unusedSet = unused.ToHashSet();
        var segmentBytes = new Dictionary<int, long>();
        foreach (var file in files)
        {
            var category = library.Fonts.Any(font => font.FileName == file.Key) ? -5 : owners.TryGetValue(file.Key, out var memberships) && memberships.Count > 0
                ? memberships.Count == 1 ? memberships.Single() : -1
                : referenced.Contains(file.Key) ? -2 : unusedSet.Contains(file.Key) ? -3 : -4;
            segmentBytes[category] = segmentBytes.GetValueOrDefault(category) + file.Value;
        }
        var segments = new List<LibraryStorageSegment>();
        foreach (var pair in segmentBytes.OrderBy(pair => pair.Key < 0 ? int.MaxValue + pair.Key : pair.Key))
        {
            var (label, color) = pair.Key switch
            {
                -5 => ("Custom fonts", "storage-color-fonts"),
                -1 => ("Shared between packs", "storage-color-shared"),
                -2 => ("Not in a pack", "storage-color-unpacked"),
                -3 => ("Unused edit files", "storage-color-unused"),
                -4 => ("Other media", "storage-color-other"),
                _ => (library.Packs[pair.Key].Name, $"pack-color-{pair.Key % 10}")
            };
            if (pair.Value > 0) segments.Add(new(label, pair.Value, color));
        }
        var indexBytes = File.Exists(Index) ? new FileInfo(Index).Length : 0;
        if (indexBytes > 0) segments.Add(new("Library index", indexBytes, "storage-color-index"));
        return new(library.Stickers.Count, library.Stickers.Count(image => image.Animated), library.Packs.Count,
            Size(current), Size(originals), Size(layers), Size(unused), unused.Count,
            indexBytes, files.Values.Sum(), Root, Size(removable), removable.Count, packs, segments) { FontBytes = Size(library.Fonts.Select(f => f.FileName)), Fonts = library.Fonts.Select(f => new FontStorageStatistics(f.Name, files.GetValueOrDefault(f.FileName))).ToList() };
    }

    private List<string> FindUnusedFiles(StickerLibrary library)
    {
        var referenced = library.Stickers.SelectMany(ImageFiles).Concat(library.Fonts.Select(f => f.FileName)).ToHashSet();
        return Directory.EnumerateFiles(Path.Combine(Root, "media")).Select(Path.GetFileName).OfType<string>()
            .Where(file => !referenced.Contains(file) && IsGeneratedMediaFile(file)).ToList();
    }

    private static bool IsGeneratedMediaFile(string file)
    {
        var token = file.Split('.')[0];
        var suffix = file[token.Length..].ToLowerInvariant();
        return Guid.TryParseExact(token, "N", out _) && suffix is ".png" or ".jpg" or ".jpeg" or ".gif" or ".webp" or ".layers.json";
    }

    private static IEnumerable<string> ImageFiles(Sticker image) =>
        new[] { image.FileName, image.OriginalFileName, image.CompositionFileName }.OfType<string>();

    private static bool IsPreparedSticker(Sticker image) => image.MimeType == "image/webp" && image.Width == 512 &&
        image.Height == 512 && image.Bytes > 0 && image.Bytes <= (image.Animated ? 500 : 100) * 1024 &&
        (!image.Animated || (image.DurationMs is > 0 and <= 10_000 && image.MinimumFrameMs >= 8));

    private async Task<string> ImageFingerprintAsync(Sticker image)
    {
        async Task<string> Hash(string? file)
        {
            if (file is null) return "none";
            await using var stream = File.OpenRead(MediaPath(file));
            return Convert.ToHexString(await SHA256.HashDataAsync(stream));
        }
        return $"{await Hash(image.FileName)}:{await Hash(image.OriginalFileName)}:{await Hash(image.CompositionFileName)}";
    }

    private async Task SaveLibrarySnapshotAsync(StickerLibrary library)
    {
        foreach (var pack in library.Packs)
            pack.Animated = library.Stickers.Any(image => image.Animated && pack.StickerIds.Contains(image.Id));
        var previous = await ReadInternalAsync();
        TrackPackUpdates(previous, library);
        var json = System.Text.Json.JsonSerializer.Serialize(library, Json);
        if (StorageLimitBytes is { } limit)
        {
            var oldFiles = previous.Stickers.SelectMany(ImageFiles).Concat(previous.Fonts.Select(f => f.FileName)).ToHashSet();
            var newFiles = library.Stickers.SelectMany(ImageFiles).Concat(library.Fonts.Select(f => f.FileName)).ToHashSet();
            var media = Directory.EnumerateFiles(Path.Combine(Root, "media")).Select(path => new FileInfo(path))
                .ToDictionary(file => file.Name, file => file.Length);
            long Size(IEnumerable<string> names) => names.Sum(name => media.GetValueOrDefault(name));
            // New files are staged before the index commit; replaced files are removed afterwards.
            // Budget the final library, allowing cleanup and smaller replacements even above the cap.
            var beforeBytes = media.Values.Sum() - Size(newFiles.Except(oldFiles)) + (File.Exists(Index) ? new FileInfo(Index).Length : 0);
            var afterBytes = media.Values.Sum() - Size(oldFiles.Except(newFiles)) + System.Text.Encoding.UTF8.GetByteCount(json);
            if (afterBytes > limit && afterBytes > beforeBytes)
                throw new InvalidOperationException("Library storage limit reached. Remove unused files, discard originals, or increase the limit in Settings → General → Library statistics & storage.");
        }
        await File.WriteAllTextAsync(Index + ".tmp", json);
        File.Move(Index + ".tmp", Index, true);
        Changed?.Invoke();
    }

    private bool TryDeleteMedia(string file)
    {
        try { File.Delete(MediaPath(file)); return true; }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }
}
