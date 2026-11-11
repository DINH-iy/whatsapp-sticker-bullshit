using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace StickerApp.Core;

/// <summary>A portable ZIP containing an ordered manifest and the current image files.</summary>
public static partial class PackArchive
{
    public const long MaxArchiveBytes = 100L * 1024 * 1024;
    private const int MaxManifestBytes = 192 * 1024;
    private const string FormatName = "sticker-studio-pack";
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private static readonly DateTimeOffset FixedTimestamp = new(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public sealed class Manifest
    {
        public bool Public { get; set; }
        public string Format { get; set; } = FormatName;
        public int Version { get; set; } = 1;
        public string Name { get; set; } = "";
        public bool Nsfw { get; set; }
        public bool Animated { get; set; }
        [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
        public string? CoverImage { get; set; }
        public string Publisher { get; set; } = "Sticker Studio";
        public List<ManifestImage> Images { get; set; } = [];
    }

    public sealed class ManifestImage
    {
        public Guid? StickerId { get; set; }
        public string Name { get; set; } = "";
        public bool Nsfw { get; set; }
        public string Tags { get; set; } = "";
        public string File { get; set; } = "";
        public string Sha256 { get; set; } = "";
        [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
        public string? CompositionFile { get; set; }
        [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
        public string? CompositionSha256 { get; set; }
    }

    public sealed record ImportedImage(string Name, string Tags, byte[] Bytes, MediaInfo Info, bool Nsfw = false, byte[]? CompositionBytes = null, Guid? SourceId = null);
    public sealed record ImportedPack(string Name, bool Animated, IReadOnlyList<ImportedImage> Images, string? CoverImage = null, string Publisher = "Sticker Studio", bool IsWhatsAppFormat = false, bool Nsfw = false, bool Public = false);
    public sealed record ExportInfo(string FileName, string Sha256);

    public static async Task<ExportInfo> WriteAsync(
        Stream destination, StickerPack pack, IReadOnlyList<Sticker> images, Func<Sticker, Stream> openImage, Func<Sticker, Stream>? openComposition = null)
    {
        if (!destination.CanSeek)
            throw new ArgumentException("Pack export requires a seekable stream.", nameof(destination));
        if (images.Count > 30 || images.Count != pack.StickerIds.Count)
            throw new InvalidDataException("The pack contains too many images or references a missing image.");

        if (pack.CoverImage is not null) PackCover.ReadBytes(pack.CoverImage);
        var manifest = new Manifest { Name = pack.Name, Animated = pack.Animated, CoverImage = pack.CoverImage, Publisher = pack.Publisher, Nsfw = pack.Nsfw, Public = pack.Public };
        manifest.Version = images.Any(image => image.CompositionFileName is not null) ? 2 : 1;
        long totalBytes = 0;

        using (var archive = new ZipArchive(destination, ZipArchiveMode.Create, leaveOpen: true))
        {
            for (var index = 0; index < images.Count; index++)
            {
                var image = images[index];
                await using var source = openImage(image);
                var bytes = await ReadBoundedAsync(source, 100 * 1024 * 1024);
                var info = LibraryStore.InspectImport(bytes, 100 * 1024 * 1024);

                totalBytes += bytes.Length;
                if (totalBytes > MaxArchiveBytes)
                    throw new InvalidDataException("Choose a pack smaller than 100 MB to export.");

                var path = $"media/{index:D2}{info.Extension}";
                await WriteEntryAsync(archive, path, bytes);
                var metadata = new ManifestImage
                {
                    StickerId = image.BackupStickerId ?? image.Id,
                    Name = image.Name,
                    Tags = image.Tags,
                    Nsfw = image.Nsfw,
                    File = path,
                    Sha256 = Convert.ToHexString(SHA256.HashData(bytes))
                };
                if (image.CompositionFileName is not null)
                {
                    if (openComposition is null) throw new InvalidDataException("Editable layer artwork is unavailable for this backup.");
                    await using var compositionSource = openComposition(image);
                    var composition = await ReadBoundedAsync(compositionSource, LayerComposition.MaxFileBytes);
                    ValidateComposition(composition);
                    totalBytes += composition.Length;
                    if (totalBytes > MaxArchiveBytes) throw new InvalidDataException("Sticker artwork and layers exceed the 100 MB pack limit.");
                    metadata.CompositionFile = $"layers/{index:D2}.layers.json";
                    metadata.CompositionSha256 = Convert.ToHexString(SHA256.HashData(composition));
                    await WriteEntryAsync(archive, metadata.CompositionFile, composition);
                }
                manifest.Images.Add(metadata);
            }

            var manifestBytes = JsonSerializer.SerializeToUtf8Bytes(manifest, Json);
            if (manifestBytes.Length > MaxManifestBytes)
                throw new InvalidDataException("The pack metadata is too large.");
            if (totalBytes + manifestBytes.Length > MaxArchiveBytes)
                throw new InvalidDataException("The unpacked pack exceeds the 100 MB pack limit.");
            await WriteEntryAsync(archive, "manifest.json", manifestBytes);
        }

        if (destination.Length > MaxArchiveBytes)
            throw new InvalidDataException("Choose a pack smaller than 100 MB to export.");

        destination.Position = 0;
        var hash = Convert.ToHexString(await SHA256.HashDataAsync(destination));
        destination.Position = 0;
        return new ExportInfo(FileNameFor(pack.Name), hash);
    }

    public static async Task<ImportedPack> ReadAsync(Stream source, int gifLimit = LibraryStore.MaxGifImportBytes)
    {
        // A bounded temporary file avoids loading an entire compressed archive into memory.
        var temporaryPath = Path.Combine(Path.GetTempPath(), $"sticker-import-{Guid.NewGuid():N}.zip");
        await using var temporary = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.ReadWrite,
            FileShare.None, 64 * 1024, FileOptions.Asynchronous | FileOptions.DeleteOnClose);
        await CopyBoundedAsync(source, temporary, MaxArchiveBytes);
        temporary.Position = 0;

        using var archive = new ZipArchive(temporary, ZipArchiveMode.Read, leaveOpen: true);
        if (archive.Entries.Count is < 1 or > 61 ||
            archive.Entries.Select(entry => entry.FullName).Distinct(StringComparer.Ordinal).Count() != archive.Entries.Count)
            throw new InvalidDataException("The pack archive contains unexpected or repeated files.");

        var manifestEntry = archive.GetEntry("manifest.json");
        if (manifestEntry is null) return await WhatsAppArchive.ReadAsync(archive);
        if (manifestEntry.Length > MaxManifestBytes)
            throw new InvalidDataException("The pack metadata is too large.");

        await using var manifestStream = manifestEntry.Open();
        var manifestBytes = await ReadBoundedAsync(manifestStream, MaxManifestBytes);
        var manifest = JsonSerializer.Deserialize<Manifest>(manifestBytes)
            ?? throw new InvalidDataException("The pack metadata is empty.");
        ValidateManifest(manifest);

        if (archive.Entries.Count != manifest.Images.Count + manifest.Images.Count(image => image.CompositionFile is not null) + 1)
            throw new InvalidDataException("The pack archive contains unlisted files.");

        List<ImportedImage> images = [];
        long totalBytes = manifestBytes.Length;
        for (var index = 0; index < manifest.Images.Count; index++)
        {
            var image = manifest.Images[index];
            var entry = archive.GetEntry(image.File)
                ?? throw new InvalidDataException($"The image “{image.Name}” is missing.");
            if (entry.Length > Math.Max(LibraryStore.MaxImportBytes, gifLimit))
                throw new InvalidDataException(LibraryStore.GifLimitError(gifLimit));
            totalBytes += entry.Length;
            if (totalBytes > MaxArchiveBytes)
                throw new InvalidDataException("The unpacked images exceed the 100 MB pack limit.");

            await using var input = entry.Open();
            var bytes = await ReadBoundedAsync(input, Math.Max(LibraryStore.MaxImportBytes, gifLimit));
            if (bytes.LongLength != entry.Length)
                throw new InvalidDataException("An image has an incorrect length.");
            var hash = Convert.ToHexString(SHA256.HashData(bytes));
            if (!string.Equals(hash, image.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"The image “{image.Name}” failed its checksum. Download the pack again.");

            var info = LibraryStore.InspectImport(bytes, gifLimit);
            if (image.File != $"media/{index:D2}{info.Extension}")
                throw new InvalidDataException("An image does not match the pack format.");

            byte[]? composition = null;
            if (image.CompositionFile is not null)
            {
                var layerEntry = archive.GetEntry(image.CompositionFile)
                    ?? throw new InvalidDataException($"The editable layers for “{image.Name}” are missing.");
                if (layerEntry.Length > LayerComposition.MaxFileBytes)
                    throw new InvalidDataException("An editable composition exceeds the 36 MB file limit.");
                totalBytes += layerEntry.Length;
                if (totalBytes > MaxArchiveBytes) throw new InvalidDataException("The unpacked pack exceeds the 100 MB pack limit.");
                await using var layerStream = layerEntry.Open();
                composition = await ReadBoundedAsync(layerStream, LayerComposition.MaxFileBytes);
                if (composition.LongLength != layerEntry.Length ||
                    !string.Equals(Convert.ToHexString(SHA256.HashData(composition)), image.CompositionSha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"The editable layers for “{image.Name}” failed their checksum. Download the pack again.");
                ValidateComposition(composition);
            }
            images.Add(new ImportedImage(image.Name.Trim(), image.Tags.Trim(), bytes, info, image.Nsfw, composition, image.StickerId));
        }

        return new ImportedPack(manifest.Name.Trim(), images.Any(image => image.Info.Animated), images, manifest.CoverImage, manifest.Publisher, Nsfw: manifest.Nsfw, Public: manifest.Public);
    }

    public static string FileNameFor(string name, bool whatsAppFormat = false)
    {
        var clean = string.Concat(name.Where(character => char.IsLetterOrDigit(character) || character is ' ' or '-' or '_')).Trim();
        return $"{(clean.Length == 0 ? "stickers" : clean)}{(whatsAppFormat ? ".wastickers" : ".stickerpack.zip")}";
    }

    private static void ValidateManifest(Manifest manifest)
    {
        if (manifest.Format != FormatName || manifest.Version is not (1 or 2))
            throw new InvalidDataException("This is not a supported sticker pack version.");
        if (string.IsNullOrWhiteSpace(manifest.Name) || manifest.Name.Trim().Length > 128 ||
            manifest.Images is null || manifest.Images.Count > 30 ||
            string.IsNullOrWhiteSpace(manifest.Publisher) || manifest.Publisher.Length > 128)
            throw new InvalidDataException("The pack name or image list is invalid.");

        if (manifest.CoverImage is not null) PackCover.ReadBytes(manifest.CoverImage);
        var paths = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < manifest.Images.Count; index++)
        {
            var image = manifest.Images[index];
            if (image is null || image.StickerId == Guid.Empty || string.IsNullOrWhiteSpace(image.Name) || image.Name.Trim().Length > 128 ||
                image.Tags is null || image.Tags.Length > 256 || image.Sha256 is null ||
                image.Sha256.Length != 64 || !image.Sha256.All(Uri.IsHexDigit) ||
                string.IsNullOrEmpty(image.File) || !paths.Add(image.File))
                throw new InvalidDataException("The pack contains invalid image metadata.");
            if ((image.CompositionFile is null) != (image.CompositionSha256 is null) ||
                (image.CompositionFile is not null && (manifest.Version != 2 ||
                    image.CompositionFile != $"layers/{index:D2}.layers.json" || !paths.Add(image.CompositionFile) ||
                    image.CompositionSha256!.Length != 64 || !image.CompositionSha256.All(Uri.IsHexDigit))))
                throw new InvalidDataException("The pack contains invalid editable layer metadata.");
        }
    }

    internal static async Task WriteEntryAsync(ZipArchive archive, string path, byte[] bytes)
    {
        var entry = archive.CreateEntry(path, CompressionLevel.NoCompression);
        // Fixed timestamps make repeat exports of unchanged content have the same hash.
        entry.LastWriteTime = FixedTimestamp;
        await using var output = entry.Open();
        await output.WriteAsync(bytes);
    }

    internal static async Task<byte[]> ReadBoundedAsync(Stream source, long maximumBytes)
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
