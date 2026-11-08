namespace StickerApp.Core;

public sealed record ImageStorageSize(long FileBytes, long TotalBytes);
public sealed record LibraryBrowseSnapshot(StickerLibrary Library, IReadOnlyDictionary<Guid, ImageStorageSize> ImageSizes,
    IReadOnlyDictionary<Guid, PackStorageStatistics> PackSizes);
public sealed record ImageBrowseOptions(string Search = "", string Type = "all", string Format = "all",
    string Dimensions = "all", string Storage = "all", string Sort = "default");
public sealed record PackBrowseOptions(string Search = "", string Type = "all", string Count = "all",
    string Storage = "all", string Sort = "default", string Ownership = "all");

public sealed partial class LibraryStore
{
    public async Task<LibraryBrowseSnapshot> ReadBrowseSnapshotAsync()
    {
        await gate.WaitAsync();
        try
        {
            var library = await ReadInternalAsync();
            var files = Directory.EnumerateFiles(Path.Combine(Root, "media")).Select(path => new FileInfo(path))
                .ToDictionary(file => file.Name, file => file.Length);
            long Size(IEnumerable<string> names) => names.Distinct().Sum(name => files.GetValueOrDefault(name));
            var images = library.Stickers.ToDictionary(image => image.Id,
                image => new ImageStorageSize(files.GetValueOrDefault(image.FileName), Size(ImageFiles(image))));
            var packs = library.Packs.ToDictionary(pack => pack.Id, pack =>
            {
                var contents = library.Stickers.Where(image => pack.StickerIds.Contains(image.Id)).ToList();
                var current = contents.Select(image => image.FileName).ToHashSet();
                return new PackStorageStatistics(pack.Id, pack.Name, contents.Count, Size(current),
                    Size(contents.SelectMany(ImageFiles).Except(current)));
            });
            return new(library, images, packs);
        }
        finally { gate.Release(); }
    }
}

public static class LibraryBrowsing
{
    public static IReadOnlyList<Sticker> Images(LibraryBrowseSnapshot snapshot, ImageBrowseOptions options,
        bool decimalUnits, Guid? packId = null)
    {
        var all = snapshot.Library.Stickers;
        var source = packId is { } id
            ? (snapshot.Library.Packs.FirstOrDefault(pack => pack.Id == id)?.StickerIds ?? [])
                .Select(stickerId => all.FirstOrDefault(image => image.Id == stickerId)).OfType<Sticker>()
            : all.AsEnumerable().Reverse();
        var search = options.Search.Trim();
        var matches = source.Where(image => (image.Name.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                image.Tags.Contains(search, StringComparison.OrdinalIgnoreCase)) &&
            (options.Type switch { "still" => !image.Animated, "animated" => image.Animated,
                "layered" => image.CompositionFileName is not null, _ => true }) &&
            (options.Format == "all" || image.MimeType == "image/" + options.Format) &&
            (options.Dimensions switch { "512" => image.Width == 512 && image.Height == 512,
                "square" => image.Width == image.Height, "landscape" => image.Width > image.Height,
                "portrait" => image.Height > image.Width, _ => true }) &&
            MatchesStorage(snapshot.ImageSizes.GetValueOrDefault(image.Id)?.TotalBytes ?? 0, options.Storage, decimalUnits, false));
        return (options.Sort switch
        {
            "name-asc" => matches.OrderBy(image => image.Name, StringComparer.OrdinalIgnoreCase),
            "name-desc" => matches.OrderByDescending(image => image.Name, StringComparer.OrdinalIgnoreCase),
            "file-desc" => matches.OrderByDescending(image => snapshot.ImageSizes.GetValueOrDefault(image.Id)?.FileBytes ?? 0),
            "file-asc" => matches.OrderBy(image => snapshot.ImageSizes.GetValueOrDefault(image.Id)?.FileBytes ?? 0),
            "storage-desc" => matches.OrderByDescending(image => snapshot.ImageSizes.GetValueOrDefault(image.Id)?.TotalBytes ?? 0),
            "storage-asc" => matches.OrderBy(image => snapshot.ImageSizes.GetValueOrDefault(image.Id)?.TotalBytes ?? 0),
            "pixels-desc" => matches.OrderByDescending(image => (long)image.Width * image.Height),
            "pixels-asc" => matches.OrderBy(image => (long)image.Width * image.Height),
            "type" => matches.OrderBy(image => image.Animated).ThenBy(image => image.MimeType).ThenBy(image => image.Name, StringComparer.OrdinalIgnoreCase),
            "oldest" when packId is null => matches.Reverse(),
            _ => matches
        }).ToList();
    }

    public static IReadOnlyList<StickerPack> Packs(LibraryBrowseSnapshot snapshot, PackBrowseOptions options, bool decimalUnits)
    {
        var search = options.Search.Trim();
        var matches = snapshot.Library.Packs.Where(pack => (pack.Name.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                pack.Publisher.Contains(search, StringComparison.OrdinalIgnoreCase)) &&
            (options.Type == "all" || PackType(pack, snapshot.Library.Stickers) == options.Type) &&
            (options.Ownership switch { "group" => pack.Collection is not null, "solo" => pack.Collection is null, _ => true }) &&
            (options.Count switch { "empty" => pack.StickerIds.Count == 0, "few" => pack.StickerIds.Count is > 0 and < 3,
                "ready" => pack.StickerIds.Count is >= 3 and <= 30, "full" => pack.StickerIds.Count == 30, _ => true }) &&
            MatchesStorage(PackBytes(snapshot, pack.Id), options.Storage, decimalUnits, true));
        return (options.Sort switch
        {
            "name-asc" => matches.OrderBy(pack => pack.Name, StringComparer.OrdinalIgnoreCase),
            "name-desc" => matches.OrderByDescending(pack => pack.Name, StringComparer.OrdinalIgnoreCase),
            "publisher" => matches.OrderBy(pack => pack.Publisher, StringComparer.OrdinalIgnoreCase).ThenBy(pack => pack.Name, StringComparer.OrdinalIgnoreCase),
            "group-first" => matches.OrderByDescending(pack => pack.Collection is not null).ThenBy(pack => pack.Name, StringComparer.OrdinalIgnoreCase),
            "solo-first" => matches.OrderBy(pack => pack.Collection is not null).ThenBy(pack => pack.Name, StringComparer.OrdinalIgnoreCase),
            "count-desc" => matches.OrderByDescending(pack => pack.StickerIds.Count),
            "count-asc" => matches.OrderBy(pack => pack.StickerIds.Count),
            "file-desc" => matches.OrderByDescending(pack => snapshot.PackSizes.GetValueOrDefault(pack.Id)?.StickerBytes ?? 0),
            "file-asc" => matches.OrderBy(pack => snapshot.PackSizes.GetValueOrDefault(pack.Id)?.StickerBytes ?? 0),
            "storage-desc" => matches.OrderByDescending(pack => PackBytes(snapshot, pack.Id)),
            "storage-asc" => matches.OrderBy(pack => PackBytes(snapshot, pack.Id)),
            "newest" => matches.Reverse(),
            _ => matches
        }).ToList();
    }

    public static string PackType(StickerPack pack, IReadOnlyList<Sticker> images)
    {
        var contents = images.Where(image => pack.StickerIds.Contains(image.Id)).ToList();
        if (contents.Count == 0) return "empty";
        var animated = contents.Count(image => image.Animated);
        return animated == 0 ? "still" : animated == contents.Count ? "animated" : "mixed";
    }

    private static long PackBytes(LibraryBrowseSnapshot snapshot, Guid id) =>
        snapshot.PackSizes.TryGetValue(id, out var size) ? size.StickerBytes + size.SourceBytes : 0;

    private static bool MatchesStorage(long bytes, string range, bool decimalUnits, bool pack)
    {
        var unit = decimalUnits ? 1000L : 1024L;
        var first = pack ? unit * unit : 100 * unit;
        var second = pack ? 5 * unit * unit : 500 * unit;
        var third = 20 * unit * unit;
        return range switch { "small" => bytes < first, "medium" => bytes >= first && bytes < second,
            "large" => bytes >= second && (!pack || bytes < third), "huge" => bytes >= third, _ => true };
    }
}

public static class StorageDisplay
{
    public static string Format(long bytes, bool decimalUnits)
    {
        var step = decimalUnits ? 1000d : 1024d;
        if (bytes < step) return $"{bytes:N0} B";
        if (bytes < step * step) return $"{bytes / step:N1} {(decimalUnits ? "KB" : "KiB")}";
        if (bytes < step * step * step) return $"{bytes / (step * step):N1} {(decimalUnits ? "MB" : "MiB")}";
        return $"{bytes / (step * step * step):N2} {(decimalUnits ? "GB" : "GiB")}";
    }
}
