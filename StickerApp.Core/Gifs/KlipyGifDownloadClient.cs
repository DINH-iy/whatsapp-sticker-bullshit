using System.Text.RegularExpressions;

namespace StickerApp.Core;

// Native hosts download on the phone itself, avoiding WebView CORS restrictions.
// The browser host uses its device-side JavaScript downloader instead.
public static class KlipyGifDownloadClient
{
    // Android's response/stream disposal can drain unread network data synchronously,
    // including when a size/status check fails. Keep the entire lifetime off the UI
    // thread, even when SendAsync or a stream read completes synchronously.
    public static Task<byte[]> DownloadAsync(HttpClient client, string url, CancellationToken cancellationToken = default, int maxBytes = LibraryStore.MaxGifImportBytes, bool decimalUnits = false)
        => Task.Run(() => DownloadCoreAsync(client, url, cancellationToken, maxBytes, decimalUnits), cancellationToken);

    public static Task<long?> InspectSizeAsync(HttpClient client, string url, CancellationToken cancellationToken = default)
        => Task.Run(async () =>
        {
            ValidateUrl(url);
            using var request = new HttpRequestMessage(HttpMethod.Head, url);
            request.Headers.CacheControl = new() { NoCache = true, NoStore = true };
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode || response.Content.Headers.ContentEncoding.Any(value => value != "identity")) return (long?)null;
            return response.Content.Headers.ContentLength is > 0 ? response.Content.Headers.ContentLength : null;
        }, cancellationToken);

    private static void ValidateUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps ||
            !uri.IsDefaultPort || uri.UserInfo.Length != 0 ||
            !Regex.IsMatch(uri.Host, @"\Astatic\d*\.klipy\.com\z", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            throw new InvalidDataException("This GIF does not have a supported KLIPY download URL.");
    }

    private static async Task<byte[]> DownloadCoreAsync(HttpClient client, string url, CancellationToken cancellationToken, int maxBytes, bool decimalUnits)
    {
        ValidateUrl(url);

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.CacheControl = new() { NoCache = true, NoStore = true };
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException("KLIPY could not deliver this GIF. Try another result.");
        if (response.Content.Headers.ContentLength > maxBytes)
            throw new InvalidDataException(LibraryStore.GifLimitError(maxBytes, decimalUnits));
        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var memory = new MemoryStream();
        var buffer = new byte[64 * 1024];
        int count;
        while ((count = await input.ReadAsync(buffer, cancellationToken)) != 0)
        {
            if (memory.Length + count > maxBytes)
                throw new InvalidDataException(LibraryStore.GifLimitError(maxBytes, decimalUnits));
            memory.Write(buffer, 0, count);
        }
        var bytes = memory.ToArray();
        if (MediaInspector.Inspect(bytes).MimeType != "image/gif")
            throw new InvalidDataException("The downloaded file is not a valid GIF.");
        cancellationToken.ThrowIfCancellationRequested();
        return bytes;
    }
}
