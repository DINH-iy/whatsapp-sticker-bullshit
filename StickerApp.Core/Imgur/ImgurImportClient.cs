using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
namespace StickerApp.Core;

public sealed record ImgurMedia(string Name, string Url, bool Video);
public sealed class ImgurImportClient : IDisposable
{
    private readonly HttpClient http;
    public ImgurImportClient() : this(new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(60) }) {}
    public ImgurImportClient(HttpClient http) => this.http = http;
    public static Uri ValidateLink(string value)
    {
        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri) || uri.Scheme != "https" || !uri.IsDefaultPort || uri.UserInfo.Length != 0 ||
            uri.Host is not ("imgur.com" or "www.imgur.com" or "i.imgur.com"))
            throw new InvalidDataException("Paste an HTTPS Imgur image, GIF, video or album link.");
        return uri;
    }
    public async Task<IReadOnlyList<ImgurMedia>> PreviewAsync(string value, string clientId, CancellationToken token)
    {
        var uri = ValidateLink(value);
        var path = uri.AbsolutePath.Trim('/');
        if (uri.Host == "i.imgur.com")
        {
            if (!Regex.IsMatch(path, @"\A[a-zA-Z0-9]+\.(png|jpe?g|webp|gif|gifv|mp4|webm)\z", RegexOptions.IgnoreCase))
                throw new InvalidDataException("Use the direct Imgur media link, including its file extension.");
            var video = path.EndsWith(".gifv", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".webm", StringComparison.OrdinalIgnoreCase);
            if (path.EndsWith(".gifv", StringComparison.OrdinalIgnoreCase)) path = path[..^5] + ".mp4";
            return [new(Path.GetFileNameWithoutExtension(path), "https://i.imgur.com/" + path, video)];
        }
        if (!Regex.IsMatch(clientId, @"\A[a-zA-Z0-9]{5,64}\z"))
            throw new InvalidDataException("Post and album links need your Imgur Client ID below. Direct i.imgur.com links work without one.");
        var parts = path.Split('/');
        var album = parts.Length == 2 && parts[0] is "a" or "gallery";
        var id = parts[^1].Split('-')[^1];
        if (!Regex.IsMatch(id, @"\A[a-zA-Z0-9]{5,12}\z") || (parts.Length > 1 && !album)) throw new InvalidDataException("Unsupported Imgur post link.");
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.imgur.com/3/" + (parts.Length == 2 && parts[0] == "gallery" ? "gallery/" : album ? "album/" : "image/") + id);
        request.Headers.TryAddWithoutValidation("Authorization", "Client-ID " + clientId);
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
        if (!response.IsSuccessStatusCode) throw new InvalidDataException("Imgur could not open this link. Check the Client ID, public link and rate limits.");
        await using var stream = await response.Content.ReadAsStreamAsync(token);
        var bytes = await ReadLimited(stream, 2 * 1024 * 1024, token);
        using var document = JsonDocument.Parse(bytes);
        var data = document.RootElement.GetProperty("data");
        var items = data.TryGetProperty("images", out var images) ? images.EnumerateArray().ToArray() : [data];
        if (items.Length > 100) throw new InvalidDataException("This album exceeds 100 items. Choose a smaller album or direct links.");
        return items.Select((item, index) => {
            var video = item.TryGetProperty("type", out var type) && type.GetString()?.StartsWith("video/") == true;
            var url = item.GetProperty("link").GetString() ?? "";
            var media = ValidateLink(url);
            if (media.Host != "i.imgur.com") throw new InvalidDataException("Imgur returned an unsupported media URL.");
            var name = item.TryGetProperty("title", out var title) ? title.GetString() : null;
            return new ImgurMedia(string.IsNullOrWhiteSpace(name) ? $"Imgur {index + 1}" : name[..Math.Min(name.Length, 128)], url, video);
        }).ToArray();
    }
    public async Task<byte[]> DownloadAsync(ImgurMedia media, CancellationToken token)
    {
        if (ValidateLink(media.Url).Host != "i.imgur.com") throw new InvalidDataException("Unsupported Imgur media host.");
        using var response = await http.GetAsync(media.Url, HttpCompletionOption.ResponseHeadersRead, token);
        if (!response.IsSuccessStatusCode) throw new InvalidDataException("Imgur could not deliver this file. It may have been removed.");
        var max = media.Video ? 100 * 1024 * 1024 : LibraryStore.MaxImportBytes;
        if (response.Content.Headers.ContentLength > max) throw new InvalidDataException("This file exceeds the import size limit.");
        await using var stream = await response.Content.ReadAsStreamAsync(token);
        var bytes = await ReadLimited(stream, max, token);
        if (!media.Video) MediaInspector.Inspect(bytes);
        return bytes;
    }
    private static async Task<byte[]> ReadLimited(Stream input, long limit, CancellationToken token)
    {
        using var output = new MemoryStream(); var buffer = new byte[65536]; int count;
        while ((count = await input.ReadAsync(buffer, token)) != 0) { if (output.Length + count > limit) throw new InvalidDataException("The response exceeds the import size limit."); output.Write(buffer, 0, count); }
        return output.ToArray();
    }
    public void Dispose() => http.Dispose();
}
