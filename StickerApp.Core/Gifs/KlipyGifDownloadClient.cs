using System.Text.RegularExpressions;

namespace StickerApp.Core;

// Native hosts download on the phone itself, avoiding WebView CORS restrictions.
// The browser host uses its device-side JavaScript downloader instead.
public static class KlipyGifDownloadClient
{
    public static async Task<byte[]> DownloadAsync(HttpClient client, string url, CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps ||
            !uri.IsDefaultPort || uri.UserInfo.Length != 0 ||
            !Regex.IsMatch(uri.Host, @"\Astatic\d*\.klipy\.com\z", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            throw new InvalidDataException("This GIF does not have a supported KLIPY download URL.");

        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.CacheControl = new() { NoCache = true, NoStore = true };
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException("KLIPY could not deliver this GIF. Try another result.");
        if (response.Content.Headers.ContentLength > LibraryStore.MaxImportBytes)
            throw new InvalidDataException("This GIF exceeds the 10 MB import limit.");
        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var memory = new MemoryStream();
        var buffer = new byte[64 * 1024];
        int count;
        while ((count = await input.ReadAsync(buffer, cancellationToken)) != 0)
        {
            if (memory.Length + count > LibraryStore.MaxImportBytes)
                throw new InvalidDataException("This GIF exceeds the 10 MB import limit.");
            memory.Write(buffer, 0, count);
        }
        var bytes = memory.ToArray();
        if (MediaInspector.Inspect(bytes).MimeType != "image/gif")
            throw new InvalidDataException("The downloaded file is not a valid GIF.");
        cancellationToken.ThrowIfCancellationRequested();
        return bytes;
    }
}
