using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Diagnostics;

namespace StickerApp.Core;

public sealed record PinterestImage(string PinId, string PinUrl, string Title, string ThumbnailUrl, string ImageUrl)
{
    public string MediaType { get; init; } = "image";
    public string? VideoUrl { get; init; }
    public long? DurationMs { get; init; }
    public bool IsVideo => MediaType == "video";
}
public sealed record PinterestSelection(string SourceUrl, int ExpectedCount, int Count, List<PinterestImage> Images)
{
    public string SourceKind { get; init; } = "selection";
    public string? Title { get; init; }
    public int UnavailableCount { get; init; }
    public bool CollectionComplete { get; init; }
}
public sealed record PinterestImportProgress(int Completed, int Total, TimeSpan? EstimatedRemaining = null, bool IsSaving = false, bool IsConvertingVideo = false);

public sealed class PinterestImportClient : IDisposable
{
    public const string Endpoint = "https://stickers.dinnyvanhuizen.com/api/pinterest/import";
    public const int MaxImages = 100;
    public const int MaxMetadataBytes = 512 * 1024;
    public const int MaxVideoBytes = 100 * 1024 * 1024;
    public const int MaxBatchBytes = 250 * 1024 * 1024;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly HttpClient http;
    private readonly bool ownsClient;

    public PinterestImportClient() : this(new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
    { Timeout = TimeSpan.FromSeconds(90) }) => ownsClient = true;

    public PinterestImportClient(HttpClient http) => this.http = http;

    public async Task<PinterestSelection> LoadAsync(string text, CancellationToken cancellationToken = default)
    {
        var url = PinterestShareLink.Parse(text);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(90));
        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint)
        { Content = JsonContent.Create(new { url = url.AbsoluteUri, acceptBoards = true, includeVideos = true }) };
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        if (!response.IsSuccessStatusCode)
        {
            var body = await ReadBoundedAsync(response.Content, 4096, timeout.Token);
            try
            {
                using var document = JsonDocument.Parse(body);
                var code = document.RootElement.GetProperty("error").GetString();
                if (code is "board_too_large" or "board_incomplete" or "unknown_board_size" or "unsupported_video" or "unsupported_selection" or "board_client_required" or "video_client_required")
                {
                    var message = document.RootElement.GetProperty("message").GetString();
                    if (message is { Length: > 0 and <= 512 }) throw new InvalidOperationException(message);
                }
            }
            catch (Exception ex) when (ex is JsonException or KeyNotFoundException) { }
        }
        EnsureSuccess(response);
        var bytes = await ReadBoundedAsync(response.Content, MaxMetadataBytes, timeout.Token);
        PinterestSelection? selection;
        try { selection = JsonSerializer.Deserialize<PinterestSelection>(bytes, Json); }
        catch (JsonException) { throw new InvalidDataException("The importer returned invalid image information. Try sharing the selection again."); }
        ValidateSelection(selection);
        return selection!;
    }

    public static void ValidateSelection(PinterestSelection? selection)
    {
        if (selection is null || selection.Images is null || selection.Count is < 1 or > MaxImages ||
            selection.ExpectedCount is < 1 or > MaxImages ||
            selection.SourceKind is not ("board" or "selection") ||
            (selection.SourceKind == "selection" && (selection.ExpectedCount != selection.Count || selection.UnavailableCount != 0)) ||
            (selection.SourceKind == "board" && (!selection.CollectionComplete || selection.Count > selection.ExpectedCount || selection.UnavailableCount != selection.ExpectedCount - selection.Count)) ||
            selection.Images.Count != selection.Count ||
            !PinterestShareLink.TryParse(selection.SourceUrl, out _) ||
            selection.Images.Any(image => !IsValidImage(image)) ||
            selection.Images.Select(image => image.PinId).Distinct(StringComparer.Ordinal).Count() != selection.Count)
            throw new InvalidDataException("The importer returned an incomplete, oversized or invalid preview. Share a smaller pin selection instead.");
    }

    private static bool IsValidImage(PinterestImage? image) => image is not null &&
        !string.IsNullOrEmpty(image.PinId) && image.PinId.Length <= 96 &&
        (image.PinId.Length <= 30 && image.PinId.All(char.IsAsciiDigit) || image.PinId.StartsWith("e1_", StringComparison.Ordinal) && image.PinId[3..].Length > 0 && image.PinId[3..].All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-')) &&
        IsCdnUrl(image.ImageUrl) && IsCdnUrl(image.ThumbnailUrl) &&
        (image.MediaType == "image" && image.VideoUrl is null || image.IsVideo && IsVideoUrl(image.VideoUrl));

    private static bool IsVideoUrl(string? value) => value?.Length <= 4096 &&
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == "https" && uri.Host == "v1.pinimg.com" &&
        uri.Port == 443 && uri.UserInfo.Length == 0 && uri.Fragment.Length == 0 && uri.AbsolutePath.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase);

    private static bool IsCdnUrl(string? value) => value?.Length <= 4096 &&
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == "https" &&
        uri.Host == "i.pinimg.com" && uri.Port == 443 && uri.UserInfo.Length == 0 && uri.Fragment.Length == 0;

    public async Task<Guid?> ImportAsync(LibraryStore store, IReadOnlyList<PinterestImage> images, Guid? packId = null,
        IProgress<PinterestImportProgress>? progress = null, CancellationToken cancellationToken = default, string? newPackName = null,
        IReadOnlyDictionary<string, bool>? keepOriginals = null, string? newPackPublisher = null,
        Func<PinterestImage, string, CancellationToken, Task<byte[]>>? convertVideo = null)
    {
        if (images.Count is < 1 or > MaxImages || images.Any(image => !IsValidImage(image)) ||
            images.Select(image => image.PinId).Distinct(StringComparer.Ordinal).Count() != images.Count)
            throw new InvalidDataException("Choose between 1 and 100 different images from the preview.");
        if (images.Any(image => image.IsVideo) && convertVideo is null)
            throw new InvalidOperationException("Video clip review is required before importing this selection.");
        var directory = Path.Combine(Path.GetTempPath(), "sticker-pinterest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(10));
        try
        {
            List<BatchImportFile> files = [];
            TimeSpan downloadTime = TimeSpan.Zero;
            long downloadedBytes = 0;
            long preparedBytes = 0;
            progress?.Report(new(0, images.Count));
            foreach (var image in images)
            {
                var started = Stopwatch.GetTimestamp();
                using var response = await http.GetAsync(image.IsVideo ? image.VideoUrl : image.ImageUrl, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
                EnsureSuccess(response);
                var path = Path.Combine(directory, image.PinId);
                var limit = (int)Math.Min(image.IsVideo ? MaxVideoBytes : LibraryStore.MaxImportBytes, MaxBatchBytes - downloadedBytes);
                downloadedBytes += await DownloadToFileAsync(response.Content, path, limit, timeout.Token);
                downloadTime += Stopwatch.GetElapsedTime(started);
                if (image.IsVideo)
                {
                    await using (var input = File.OpenRead(path))
                    {
                        var header = new byte[12];
                        await input.ReadExactlyAsync(header, timeout.Token);
                        if (!header.AsSpan(4, 4).SequenceEqual("ftyp"u8)) throw new InvalidDataException("Pinterest returned an invalid MP4 video.");
                    }
                    progress?.Report(new(files.Count, images.Count, IsConvertingVideo: true));
                    var converted = await convertVideo!(image, path, timeout.Token);
                    timeout.Token.ThrowIfCancellationRequested();
                    if (converted.Length > LibraryStore.MaxGifImportBytes) throw new InvalidDataException("The converted GIF exceeds 20 MiB. Choose a shorter clip.");
                    var info = MediaInspector.Inspect(converted);
                    if (!info.Animated || info.MimeType is not ("image/gif" or "image/webp") || info.DurationMs > 10_100)
                        throw new InvalidDataException("Choose an animated clip of at most 10 seconds.");
                    await File.WriteAllBytesAsync(path, converted, timeout.Token);
                }
                else MediaInspector.Inspect(await File.ReadAllBytesAsync(path, timeout.Token));
                preparedBytes += new FileInfo(path).Length;
                if (preparedBytes > MaxBatchBytes) throw new InvalidDataException("The prepared batch exceeds 250 MB. Select fewer items or shorter video clips.");
                var title = string.IsNullOrWhiteSpace(image.Title) ? $"Pinterest {(image.IsVideo ? "video" : "image")} {files.Count + 1}" : image.Title.Trim();
                files.Add(new(title[..Math.Min(128, title.Length)], path,
                    keepOriginals is not null && keepOriginals.TryGetValue(image.PinId, out var keep) ? keep : null));
                // Wait for two completed downloads before extrapolating; the estimate
                // covers downloading only, since the final batch commit is separate.
                var remaining = images.Count - files.Count;
                TimeSpan? estimate = files.Count >= 2 && remaining > 0
                    ? downloadTime / files.Count * remaining : null;
                progress?.Report(new(files.Count, images.Count, estimate, remaining == 0));
            }
            return await store.ImportBatchAsync(files, packId, timeout.Token, newPackName, newPackPublisher);
        }
        finally
        {
            // Cleanup must not turn an already committed import into a reported failure.
            try { Directory.Delete(directory, true); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                System.Diagnostics.Debug.WriteLine($"Pinterest temporary download cleanup: {exception.Message}");
            }
        }
    }

    private static void EnsureSuccess(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode) return;
        throw new HttpRequestException(response.StatusCode switch
        {
            HttpStatusCode.TooManyRequests => "The Pinterest importer is busy. Wait a few seconds and try again.",
            HttpStatusCode.UnprocessableEntity or HttpStatusCode.BadRequest => "This selection is unavailable or incomplete. Select pins on a public board and share them again.",
            HttpStatusCode.GatewayTimeout => "Pinterest took too long to respond. Try again.",
            _ => "The Pinterest importer or image is unavailable. Check your connection and try again."
        }, null, response.StatusCode);
    }

    private static async Task<byte[]> ReadBoundedAsync(HttpContent content, int limit, CancellationToken token)
    {
        if (content.Headers.ContentLength > limit)
            throw new InvalidDataException($"The download exceeds the {limit / 1024:N0} KB limit.");
        await using var input = await content.ReadAsStreamAsync(token);
        using var output = new MemoryStream();
        var buffer = new byte[32 * 1024];
        int read;
        while ((read = await input.ReadAsync(buffer, token)) != 0)
        {
            if (output.Length + read > limit)
                throw new InvalidDataException($"The download exceeds the {limit / 1024:N0} KB limit.");
            output.Write(buffer, 0, read);
        }
        return output.ToArray();
    }

    private static async Task<long> DownloadToFileAsync(HttpContent content, string path, int limit, CancellationToken token)
    {
        if (limit <= 0 || content.Headers.ContentLength > limit)
            throw new InvalidDataException("Pinterest import limit reached: 10 MB per image, 100 MB per video and 250 MB per batch.");
        await using var input = await content.ReadAsStreamAsync(token);
        await using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
        var buffer = new byte[64 * 1024];
        int read;
        while ((read = await input.ReadAsync(buffer, token)) != 0)
        {
            if (output.Length + read > limit) throw new InvalidDataException("Pinterest import limit reached: 10 MB per image, 100 MB per video and 250 MB per batch.");
            await output.WriteAsync(buffer.AsMemory(0, read), token);
        }
        return output.Length;
    }

    public void Dispose()
    {
        if (ownsClient) http.Dispose();
    }
}
