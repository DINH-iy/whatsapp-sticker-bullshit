using System.Net;
using System.Text.RegularExpressions;

namespace StickerApp.Core;

/// <summary>Downloads only explicitly selected media. Account ZIP/JSON stays in the client.</summary>
public sealed class DiscordImportClient : IDisposable
{
    public const long MaxBatchBytes = 250L * 1024 * 1024;
    public const int MaxSelection = 30;
    private readonly HttpClient http;
    public DiscordImportClient() : this(new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromMinutes(2) }) { }
    public DiscordImportClient(HttpClient http) => this.http = http;
    public static Uri ValidateMediaUrl(string value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > 4096 || !Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != "https" || !uri.IsDefaultPort || uri.UserInfo.Length != 0 ||
            !Regex.IsMatch(uri.IdnHost, @"\A(?:(?:[a-z0-9-]+\.)*tenor\.(?:com|co)|(?:static\d*|media\d*)\.klipy\.com|(?:[a-z0-9-]+\.)*giphy\.com|i\.imgur\.com|cdn\.discordapp\.com|media\.discordapp\.net|images-ext-[12]\.discordapp\.net)\z", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            throw new InvalidDataException("This favourite has no supported direct media URL.");
        return uri;
    }
    public async Task<DiscordMediaDownload> DownloadAsync(DiscordFavorite favorite, long remainingBytes, CancellationToken token)
    {
        var url = ValidateMediaUrl(favorite.MediaUrl);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromMinutes(2));
        using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound or HttpStatusCode.Gone)
            throw new InvalidDataException("A favourite’s media link expired or was removed. Open it in Discord and use a fresh export. Nothing from this batch was saved.");
        if (!response.IsSuccessStatusCode) throw new InvalidDataException("A GIF provider could not deliver this favourite. Try again later or deselect it.");
        var mime = response.Content.Headers.ContentType?.MediaType?.ToLowerInvariant() ?? "";
        if (mime.StartsWith("text/") || mime.Contains("json")) throw new InvalidDataException("A favourite returned a web page instead of media. Open the original link in Discord and export again.");
        var video = mime is "video/mp4" or "video/webm" || favorite.Video && !mime.StartsWith("image/");
        var limit = Math.Min(remainingBytes, video ? LibraryStore.MaxVideoImportBytes : LibraryStore.MaxImportBytes);
        if (limit <= 0 || response.Content.Headers.ContentLength > limit) throw new InvalidDataException("This media exceeds the 10 MB image, 20 MB video or 250 MB batch limit.");
        await using var input = await response.Content.ReadAsStreamAsync(timeout.Token);
        using var memory = new MemoryStream(); var buffer = new byte[65536]; int count;
        while ((count = await input.ReadAsync(buffer, timeout.Token)) != 0)
        {
            if (memory.Length + count > limit) throw new InvalidDataException("This media exceeds the import size limit.");
            memory.Write(buffer, 0, count);
        }
        var bytes = memory.ToArray();
        try { MediaInspector.Inspect(bytes); return new(bytes, false); }
        catch (InvalidDataException) when (video)
        {
            var mp4 = bytes.Length >= 12 && bytes.AsSpan(4, 4).SequenceEqual("ftyp"u8);
            var webm = bytes.Length >= 4 && bytes.AsSpan(0, 4).SequenceEqual(new byte[] { 0x1a, 0x45, 0xdf, 0xa3 });
            if (!mp4 && !webm) throw new InvalidDataException("This favourite is not a supported GIF, image, MP4 or WebM video.");
            return new(bytes, true);
        }
    }
    public async Task<Guid?> ImportAsync(LibraryStore store, IReadOnlyList<DiscordFavorite> favorites, Guid? packId,
        Func<DiscordFavorite, byte[], CancellationToken, Task<byte[]>> reviewVideo,
        IProgress<DiscordImportProgress>? progress, CancellationToken token,
        string? newPackName = null, string? newPackPublisher = null, IReadOnlyDictionary<string, bool>? keepOriginals = null)
    {
        if (favorites.Count is < 1 or > MaxSelection) throw new InvalidDataException("Choose between 1 and 30 favourites per batch.");
        foreach (var favorite in favorites) ValidateMediaUrl(favorite.MediaUrl);
        var library = await store.ReadAsync();
        if (packId is { } id)
        {
            var pack = library.Packs.FirstOrDefault(p => p.Id == id) ?? throw new InvalidDataException("This pack no longer exists.");
            if (pack.StickerIds.Count + favorites.Count > 30) throw new InvalidDataException($"This pack has room for {30 - pack.StickerIds.Count} more stickers.");
        }
        if (newPackName is not null && library.Packs.Count >= 10) throw new InvalidDataException("This library already has 10 packs. Choose an existing pack or My Library.");
        var folder = Path.Combine(Path.GetTempPath(), "stik-discord-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        long downloaded = 0, staged = 0; var completed = 0; var files = new List<BatchImportFile>();
        try
        {
            progress?.Report(new(0, favorites.Count));
            foreach (var favorite in favorites)
            {
                token.ThrowIfCancellationRequested();
                var media = await DownloadAsync(favorite, MaxBatchBytes - downloaded, token);
                downloaded += media.Bytes.Length; var bytes = media.Bytes;
                if (media.Video)
                {
                    progress?.Report(new(completed, favorites.Count, ReviewingVideo: true));
                    bytes = await reviewVideo(favorite, bytes, token);
                }
                token.ThrowIfCancellationRequested();
                var info = MediaInspector.Inspect(bytes);
                staged += bytes.Length;
                if (staged > MaxBatchBytes) throw new InvalidDataException("Converted media exceeds the 250 MB batch limit.");
                var path = Path.Combine(folder, Guid.NewGuid().ToString("N") + info.Extension);
                await File.WriteAllBytesAsync(path, bytes, token);
                bool? keep = keepOriginals is not null && keepOriginals.TryGetValue(favorite.Id, out var choice) ? choice : null;
                files.Add(new(favorite.Name, path, keep));
                progress?.Report(new(++completed, favorites.Count));
            }
            token.ThrowIfCancellationRequested();
            progress?.Report(new(completed, favorites.Count, Saving: true));
            return await store.ImportBatchAsync(files, packId, token, newPackName, newPackPublisher);
        }
        finally { try { Directory.Delete(folder, true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
    }
    public void Dispose() => http.Dispose();
}
