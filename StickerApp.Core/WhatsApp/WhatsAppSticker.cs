namespace StickerApp.Core;

/// <summary>Build the animated export copy while leaving the library's original image untouched.</summary>
public static class WhatsAppSticker
{
    public static byte[] PrepareForExport(ReadOnlySpan<byte> source)
    {
        var bytes = MediaInspector.AsAnimatedSticker(source);
        var info = MediaInspector.Inspect(bytes);
        if (info.Width != 512 || info.Height != 512)
            throw new InvalidDataException("This image is still being prepared. Check its sticker details before exporting.");
        if (bytes.Length > 500 * 1024)
            throw new InvalidDataException("This exported sticker exceeds 500 KB. Open its details and trim it in Edit.");
        if (!info.Animated || info.FrameCount < 2 || info.DurationMs is <= 0 or > 10_000 || info.MinimumFrameMs < 8)
            throw new InvalidDataException("This animation needs at least two frames, a maximum duration of 10 seconds, and at least 8 ms per frame.");
        return bytes;
    }
}
