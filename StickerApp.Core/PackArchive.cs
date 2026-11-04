using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace StickerApp.Core;

/// <summary>A portable ZIP containing an ordered manifest and the current image files.</summary>
public static class PackArchive
{
    public const long MaxArchiveBytes = 100L * 1024 * 1024;
    private const int MaxManifestBytes = 192 * 1024;
    private const string FormatName = "sticker-studio-pack";
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private static readonly DateTimeOffset FixedTimestamp = new(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public sealed class Manifest
    {
        public string Format { get; set; } = FormatName;
        public int Version { get; set; } = 1;
        public string Name { get; set; } = "";
        public bool Animated { get; set; }
        [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
        public string? CoverImage { get; set; }
        public List<ManifestImage> Images { get; set; } = [];
    }

    public sealed class ManifestImage
    {
        public string Name { get; set; } = "";
        public string Tags { get; set; } = "";
        public string File { get; set; } = "";
        public string Sha256 { get; set; } = "";
    }

    public sealed record ImportedImage(string Name, string Tags, byte[] Bytes, MediaInfo Info);
    public sealed record ImportedPack(string Name, bool Animated, IReadOnlyList<ImportedImage> Images, string? CoverImage = null);
    public sealed record ExportInfo(string FileName, string Sha256);

    public static async Task<ExportInfo> WriteAsync(
        Stream destination, StickerPack pack, IReadOnlyList<Sticker> images, Func<Sticker, Stream> openImage)
    {
        if (!destination.CanSeek)
            throw new ArgumentException("Pack export requires a seekable stream.", nameof(destination));
        if (images.Count > 30 || images.Count != pack.StickerIds.Count)
            throw new InvalidDataException("The pack contains too many images or references a missing image.");

        if (pack.CoverImage is not null) PackCover.ReadBytes(pack.CoverImage);
        var manifest = new Manifest { Name = pack.Name, Animated = pack.Animated, CoverImage = pack.CoverImage };
        long totalBytes = 0;

        using (var archive = new ZipArchive(destination, ZipArchiveMode.Create, leaveOpen: true))
        {
            for (var index = 0; index < images.Count; index++)
            {
                var image = images[index];
                await using var source = openImage(image);
                var bytes = await ReadBoundedAsync(source, LibraryStore.MaxImportBytes);
                var info = MediaInspector.Inspect(bytes);
                if (info.Animated != pack.Animated)
                    throw new InvalidDataException("The pack mixes static and animated images.");

                totalBytes += bytes.Length;
                if (totalBytes > MaxArchiveBytes)
                    throw new InvalidDataException("Choose a pack smaller than 100 MB to export.");

                var path = $"media/{index:D2}{info.Extension}";
                await WriteEntryAsync(archive, path, bytes);
                manifest.Images.Add(new ManifestImage
                {
                    Name = image.Name,
                    Tags = image.Tags,
                    File = path,
                    Sha256 = Convert.ToHexString(SHA256.HashData(bytes))
                });
            }

            var manifestBytes = JsonSerializer.SerializeToUtf8Bytes(manifest, Json);
            if (manifestBytes.Length > MaxManifestBytes)
                throw new InvalidDataException("The pack metadata is too large.");
            await WriteEntryAsync(archive, "manifest.json", manifestBytes);
        }

        if (destination.Length > MaxArchiveBytes)
            throw new InvalidDataException("Choose a pack smaller than 100 MB to export.");

        destination.Position = 0;
        var hash = Convert.ToHexString(await SHA256.HashDataAsync(destination));
        destination.Position = 0;
        return new ExportInfo(FileNameFor(pack.Name), hash);
    }

    public static async Task<ImportedPack> ReadAsync(Stream source)
    {
        // A bounded temporary file avoids loading an entire compressed archive into memory.
        var temporaryPath = Path.Combine(Path.GetTempPath(), $"sticker-import-{Guid.NewGuid():N}.zip");
        await using var temporary = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.ReadWrite,
            FileShare.None, 64 * 1024, FileOptions.Asynchronous | FileOptions.DeleteOnClose);
        await CopyBoundedAsync(source, temporary, MaxArchiveBytes);
        temporary.Position = 0;

        using var archive = new ZipArchive(temporary, ZipArchiveMode.Read, leaveOpen: true);
        if (archive.Entries.Count is < 1 or > 31 ||
            archive.Entries.Select(entry => entry.FullName).Distinct(StringComparer.Ordinal).Count() != archive.Entries.Count)
            throw new InvalidDataException("The pack archive contains unexpected or repeated files.");

        var manifestEntry = archive.GetEntry("manifest.json")
            ?? throw new InvalidDataException("This ZIP is not a sticker pack: manifest.json is missing.");
        if (manifestEntry.Length > MaxManifestBytes)
            throw new InvalidDataException("The pack metadata is too large.");

        await using var manifestStream = manifestEntry.Open();
        var manifestBytes = await ReadBoundedAsync(manifestStream, MaxManifestBytes);
        var manifest = JsonSerializer.Deserialize<Manifest>(manifestBytes)
            ?? throw new InvalidDataException("The pack metadata is empty.");
        ValidateManifest(manifest);

        if (archive.Entries.Count != manifest.Images.Count + 1)
            throw new InvalidDataException("The pack archive contains unlisted files.");

        List<ImportedImage> images = [];
        long totalBytes = manifestBytes.Length;
        for (var index = 0; index < manifest.Images.Count; index++)
        {
            var image = manifest.Images[index];
            var entry = archive.GetEntry(image.File)
                ?? throw new InvalidDataException($"The image “{image.Name}” is missing.");
            if (entry.Length > LibraryStore.MaxImportBytes)
                throw new InvalidDataException("A pack image exceeds the 10 MB image limit.");
            totalBytes += entry.Length;
            if (totalBytes > MaxArchiveBytes)
                throw new InvalidDataException("The unpacked images exceed the 100 MB pack limit.");

            await using var input = entry.Open();
            var bytes = await ReadBoundedAsync(input, LibraryStore.MaxImportBytes);
            if (bytes.LongLength != entry.Length)
                throw new InvalidDataException("An image has an incorrect length.");
            var hash = Convert.ToHexString(SHA256.HashData(bytes));
            if (!string.Equals(hash, image.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"The image “{image.Name}” failed its checksum. Download the pack again.");

            var info = MediaInspector.Inspect(bytes);
            if (info.Animated != manifest.Animated || image.File != $"media/{index:D2}{info.Extension}")
                throw new InvalidDataException("An image does not match the pack format.");

            images.Add(new ImportedImage(image.Name.Trim(), image.Tags.Trim(), bytes, info));
        }

        return new ImportedPack(manifest.Name.Trim(), manifest.Animated, images, manifest.CoverImage);
    }

    public static string FileNameFor(string name)
    {
        var clean = string.Concat(name.Where(character => char.IsLetterOrDigit(character) || character is ' ' or '-' or '_')).Trim();
        return $"{(clean.Length == 0 ? "stickers" : clean)}.stickerpack.zip";
    }

    private static void ValidateManifest(Manifest manifest)
    {
        if (manifest.Format != FormatName || manifest.Version != 1)
            throw new InvalidDataException("This is not a supported sticker pack version.");
        if (string.IsNullOrWhiteSpace(manifest.Name) || manifest.Name.Trim().Length > 128 ||
            manifest.Images is null || manifest.Images.Count > 30)
            throw new InvalidDataException("The pack name or image list is invalid.");

        if (manifest.CoverImage is not null) PackCover.ReadBytes(manifest.CoverImage);
        var paths = new HashSet<string>(StringComparer.Ordinal);
        foreach (var image in manifest.Images)
        {
            if (image is null || string.IsNullOrWhiteSpace(image.Name) || image.Name.Trim().Length > 128 ||
                image.Tags is null || image.Tags.Length > 256 || image.Sha256 is null ||
                image.Sha256.Length != 64 || !image.Sha256.All(Uri.IsHexDigit) ||
                string.IsNullOrEmpty(image.File) || !paths.Add(image.File))
                throw new InvalidDataException("The pack contains invalid image metadata.");
        }
    }

    private static async Task WriteEntryAsync(ZipArchive archive, string path, byte[] bytes)
    {
        var entry = archive.CreateEntry(path, CompressionLevel.NoCompression);
        // Fixed timestamps make repeat exports of unchanged content have the same hash.
        entry.LastWriteTime = FixedTimestamp;
        await using var output = entry.Open();
        await output.WriteAsync(bytes);
    }

    private static async Task<byte[]> ReadBoundedAsync(Stream source, long maximumBytes)
    {
        using var buffer = new MemoryStream();
        await CopyBoundedAsync(source, buffer, maximumBytes);
        return buffer.ToArray();
    }

    private static async Task CopyBoundedAsync(Stream source, Stream destination, long maximumBytes)
    {
        var buffer = new byte[64 * 1024];
        long total = 0;
        int count;
        while ((count = await source.ReadAsync(buffer)) > 0)
        {
            total += count;
            if (total > maximumBytes)
                throw new InvalidDataException($"The file exceeds the allowed size ({maximumBytes / 1024 / 1024} MB).");
            await destination.WriteAsync(buffer.AsMemory(0, count));
        }
    }
}
