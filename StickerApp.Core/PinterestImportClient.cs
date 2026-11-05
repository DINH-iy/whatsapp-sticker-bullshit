using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace StickerApp.Core;

public sealed record PinterestImage(string PinId, string PinUrl, string Title, string ThumbnailUrl, string ImageUrl);
public sealed record PinterestSelection(string SourceUrl, int ExpectedCount, int Count, List<PinterestImage> Images);
public sealed record PinterestImportProgress(int Completed, int Total);

public sealed class PinterestImportClient : IDisposable
{
    public const string Endpoint = "https://stickers.dinnyvanhuizen.com/api/pinterest/import";
    public const int MaxImages = 100;
    public const int MaxMetadataBytes = 512 * 1024;
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
        { Content = JsonContent.Create(new { url = url.AbsoluteUri }) };
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
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
            selection.ExpectedCount != selection.Count || selection.Images.Count != selection.Count ||
            !PinterestShareLink.TryParse(selection.SourceUrl, out _) ||
            selection.Images.Any(image => !IsValidImage(image)) ||
            selection.Images.Select(image => image.PinId).Distinct(StringComparer.Ordinal).Count() != selection.Count)
            throw new InvalidDataException("The importer returned an incomplete or invalid selection. Share the pins again.");
    }

    private static bool IsValidImage(PinterestImage? image) => image is not null &&
        !string.IsNullOrEmpty(image.PinId) && image.PinId.Length <= 30 && image.PinId.All(char.IsAsciiDigit) &&
        IsCdnUrl(image.ImageUrl) && IsCdnUrl(image.ThumbnailUrl);

    private static bool IsCdnUrl(string? value) => value?.Length <= 4096 &&
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == "https" &&
        uri.Host == "i.pinimg.com" && uri.Port == 443 && uri.UserInfo.Length == 0 && uri.Fragment.Length == 0;

    public async Task<Guid?> ImportAsync(LibraryStore store, IReadOnlyList<PinterestImage> images, Guid? packId = null,
        IProgress<PinterestImportProgress>? progress = null, CancellationToken cancellationToken = default, string? newPackName = null)
    {
        if (images.Count is < 1 or > MaxImages || images.Any(image => !IsValidImage(image)) ||
            images.Select(image => image.PinId).Distinct(StringComparer.Ordinal).Count() != images.Count)
            throw new InvalidDataException("Choose between 1 and 100 different images from the preview.");
        var directory = Path.Combine(Path.GetTempPath(), "sticker-pinterest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(10));
        try
        {
            List<BatchImportFile> files = [];
            progress?.Report(new(0, images.Count));
            foreach (var image in images)
            {
                using var response = await http.GetAsync(image.ImageUrl, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
                EnsureSuccess(response);
                var bytes = await ReadBoundedAsync(response.Content, LibraryStore.MaxImportBytes, timeout.Token);
                MediaInspector.Inspect(bytes);
                var path = Path.Combine(directory, image.PinId);
                await File.WriteAllBytesAsync(path, bytes, timeout.Token);
                var title = string.IsNullOrWhiteSpace(image.Title) ? $"Pinterest {image.PinId}" : image.Title.Trim();
                files.Add(new(title[..Math.Min(128, title.Length)], path));
                progress?.Report(new(files.Count, images.Count));
            }
            return await store.ImportBatchAsync(files, packId, timeout.Token, newPackName);
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

    public void Dispose()
    {
        if (ownsClient) http.Dispose();
    }
}
