using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace StickerApp.Core;

/// <summary>The .wastickers layout: root WebP files, a PNG cover, title.txt and author.txt.</summary>
public static class WhatsAppArchive
{
    public static async Task<PackArchive.ExportInfo> WriteAsync(Stream destination, StickerPack pack,
        IReadOnlyList<Sticker> images, Func<Sticker, Stream> openImage, byte[] cover)
    {
        if (images.Count is < 1 or > 30 || images.Count != pack.StickerIds.Count)
            throw new InvalidDataException("Choose a pack with 1–30 stickers to share.");
        var coverInfo = MediaInspector.Inspect(cover);
        if (coverInfo.MimeType != "image/png" || coverInfo.Width != 96 || coverInfo.Height != 96 || cover.Length > 50 * 1024)
            throw new InvalidDataException("The pack cover must be a 96 × 96 PNG smaller than 50 KB.");
        using (var archive = new ZipArchive(destination, ZipArchiveMode.Create, leaveOpen: true))
        {
            for (var index = 0; index < images.Count; index++)
            {
                await using var source = openImage(images[index]);
                var bytes = await PackArchive.ReadBoundedAsync(source, LibraryStore.MaxImportBytes);
                var info = MediaInspector.Inspect(bytes);
                if (info.MimeType != "image/webp")
                    throw new InvalidDataException($"Prepare “{images[index].Name}” first using Prepare sticker in the pack, or choose Editable backup (.zip) to keep the original files.");
                bytes = WhatsAppSticker.PrepareForExport(bytes);
                await PackArchive.WriteEntryAsync(archive, $"{index:D2}.webp", bytes);
            }
            await PackArchive.WriteEntryAsync(archive, "cover.png", cover);
            await PackArchive.WriteEntryAsync(archive, "title.txt", Encoding.UTF8.GetBytes(pack.Name + "\n"));
            await PackArchive.WriteEntryAsync(archive, "author.txt", Encoding.UTF8.GetBytes(pack.Publisher + "\n"));
        }
        if (destination.Length > PackArchive.MaxArchiveBytes)
            throw new InvalidDataException("Choose a pack smaller than 100 MB.");
        destination.Position = 0;
        var hash = Convert.ToHexString(await SHA256.HashDataAsync(destination));
        destination.Position = 0;
        return new(PackArchive.FileNameFor(pack.Name, true), hash);
    }

    public static async Task<PackArchive.ImportedPack> ReadAsync(ZipArchive archive)
    {
        // Some sticker makers use a leading slash. Never extract archive paths to disk.
        var entries = new Dictionary<string, ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase);
        long total = 0;
        foreach (var entry in archive.Entries)
        {
            var name = entry.FullName.TrimStart('/');
            if (name.Length == 0 || name.Contains('/') || name.Contains('\\') || !entries.TryAdd(name, entry))
                throw new InvalidDataException("This sticker pack contains invalid or repeated file names.");
            total += entry.Length;
            if (entry.Length > LibraryStore.MaxImportBytes || total > PackArchive.MaxArchiveBytes)
                throw new InvalidDataException("The unpacked sticker pack is too large.");
        }
        async Task<string> ReadText(string name, bool required)
        {
            if (!entries.TryGetValue(name, out var entry))
            {
                if (required) throw new InvalidDataException($"This pack is missing {name}.");
                return "Sticker Studio";
            }
            await using var input = entry.Open();
            var text = Encoding.UTF8.GetString(await PackArchive.ReadBoundedAsync(input, 1024)).Trim();
            if (text.Length is < 1 or > 128) throw new InvalidDataException($"The pack's {name} is invalid.");
            return text;
        }
        var title = await ReadText("title.txt", true);
        var author = await ReadText("author.txt", false);
        List<PackArchive.ImportedImage> images = [];
        string? cover = null;
        foreach (var (name, entry) in entries)
        {
            if (name.Equals("title.txt", StringComparison.OrdinalIgnoreCase) || name.Equals("author.txt", StringComparison.OrdinalIgnoreCase)) continue;
            await using var input = entry.Open();
            var bytes = await PackArchive.ReadBoundedAsync(input, LibraryStore.MaxImportBytes);
            var info = MediaInspector.Inspect(bytes);
            if (name.EndsWith(".webp", StringComparison.OrdinalIgnoreCase) && info.MimeType == "image/webp")
                images.Add(new(Path.GetFileNameWithoutExtension(name), "", bytes, info));
            else if (name.EndsWith(".png", StringComparison.OrdinalIgnoreCase) && info.MimeType == "image/png" && cover is null)
                cover = PackCover.FromBytes(bytes);
            else throw new InvalidDataException("The .wastickers file contains an unexpected image or file.");
        }
        if (images.Count is < 1 or > 30) throw new InvalidDataException("A shared pack must contain 1–30 WebP stickers.");
        return new(title, images.Any(image => image.Info.Animated), images, cover, author, true);
    }
}
