namespace StickerApp.Core;

// Covers are small, separate artwork. They never count toward the pack's 30 stickers.
public static class PackCover
{
    public const int MaxBytes = 64 * 1024;

    public static string FromBytes(byte[] bytes)
    {
        if (bytes.Length is 0 or > MaxBytes)
            throw new InvalidDataException("Choose a cover smaller than 64 KB.");
        var info = MediaInspector.Inspect(bytes);
        if (info.Animated || info.Width > 512 || info.Height > 512 ||
            info.MimeType is not ("image/jpeg" or "image/png" or "image/webp"))
            throw new InvalidDataException("Use a still JPEG, PNG, or WebP cover up to 512 × 512 pixels.");
        return $"data:{info.MimeType};base64,{Convert.ToBase64String(bytes)}";
    }

    public static byte[] ReadBytes(string dataUrl)
    {
        if (dataUrl.Length > MaxBytes * 2)
            throw new InvalidDataException("The pack cover is too large.");
        var separator = dataUrl.IndexOf(',');
        if (separator < 0)
            throw new InvalidDataException("The pack cover is invalid.");
        byte[] bytes;
        try { bytes = Convert.FromBase64String(dataUrl[(separator + 1)..]); }
        catch (FormatException) { throw new InvalidDataException("The pack cover is invalid."); }
        if (FromBytes(bytes) != dataUrl)
            throw new InvalidDataException("The pack cover format is invalid.");
        return bytes;
    }
}
