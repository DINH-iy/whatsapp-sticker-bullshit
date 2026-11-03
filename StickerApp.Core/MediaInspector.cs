using System.Buffers.Binary;
using System.Text;

namespace StickerApp.Core;

public sealed record MediaInfo(string MimeType, string Extension, int Width, int Height, bool Animated, int DurationMs = 0, int MinimumFrameMs = 0);

public static class MediaInspector
{
    // Inspect the bytes, not a browser-supplied MIME type or file extension.
    public static MediaInfo Inspect(ReadOnlySpan<byte> data)
    {
        if (data.Length > 3 && data[0] == 255 && data[1] == 216)
        {
            for (int offset = 2; offset + 4 <= data.Length;)
            {
                if (data[offset++] != 255) throw new InvalidDataException("Invalid JPEG.");
                while (offset < data.Length && data[offset] == 255) offset++;
                if (offset >= data.Length) break;
                int marker = data[offset++];
                if (marker is 216 or 1 || marker is >= 208 and <= 215) continue;
                if (marker is 217 or 218 || offset + 2 > data.Length) break;
                int length = BinaryPrimitives.ReadUInt16BigEndian(data[offset..]);
                if (length < 2 || offset + length > data.Length) break;
                if (marker is >= 192 and <= 195 or >= 197 and <= 199 or >= 201 and <= 203 or >= 205 and <= 207)
                {
                    if (length < 8) break;
                    return Check(new("image/jpeg", ".jpg", BinaryPrimitives.ReadUInt16BigEndian(data[(offset + 5)..]), BinaryPrimitives.ReadUInt16BigEndian(data[(offset + 3)..]), false));
                }
                offset += length;
            }
            throw new InvalidDataException("Invalid JPEG.");
        }
        if (data.Length >= 24 && data[..8].SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }))
        {
            if (data.IndexOf("acTL"u8) >= 0) throw new InvalidDataException("Animated PNG is not supported yet. Import GIF or animated WebP.");
            return Check(new("image/png", ".png", BinaryPrimitives.ReadInt32BigEndian(data[16..]), BinaryPrimitives.ReadInt32BigEndian(data[20..]), false));
        }
        if (data.Length >= 13 && (data[..6].SequenceEqual("GIF89a"u8) || data[..6].SequenceEqual("GIF87a"u8)))
            return Check(new("image/gif", ".gif", BinaryPrimitives.ReadUInt16LittleEndian(data[6..]), BinaryPrimitives.ReadUInt16LittleEndian(data[8..]), true));
        if (data.Length < 20 || !data[..4].SequenceEqual("RIFF"u8) || !data.Slice(8, 4).SequenceEqual("WEBP"u8))
            throw new InvalidDataException("Supported formats: JPEG, PNG, GIF, WebP.");
        if ((long)BinaryPrimitives.ReadUInt32LittleEndian(data[4..]) + 8 != data.Length)
            throw new InvalidDataException("The WebP file is incomplete or invalid.");
        int width = 0, height = 0, duration = 0, minimum = int.MaxValue, frames = 0;
        bool animated = false;
        for (int offset = 12; offset + 8 <= data.Length;)
        {
            var length = BinaryPrimitives.ReadUInt32LittleEndian(data[(offset + 4)..]);
            if (length > int.MaxValue || offset + 8L + length > data.Length) throw new InvalidDataException("Invalid WebP chunk.");
            var body = data.Slice(offset + 8, (int)length);
            var type = Encoding.ASCII.GetString(data.Slice(offset, 4));
            if (type == "VP8X" && body.Length >= 10)
            {
                animated = (body[0] & 2) != 0;
                width = Read24(body[4..]) + 1; height = Read24(body[7..]) + 1;
            }
            else if (type == "VP8 " && body.Length >= 10 && width == 0)
            {
                if (!body.Slice(3, 3).SequenceEqual(new byte[] { 157, 1, 42 })) throw new InvalidDataException("Invalid WebP frame.");
                width = BinaryPrimitives.ReadUInt16LittleEndian(body[6..]) & 0x3fff;
                height = BinaryPrimitives.ReadUInt16LittleEndian(body[8..]) & 0x3fff;
            }
            else if (type == "VP8L" && body.Length >= 5 && body[0] == 47 && width == 0)
            {
                var bits = BinaryPrimitives.ReadUInt32LittleEndian(body[1..]);
                width = (int)(bits & 0x3fff) + 1; height = (int)((bits >> 14) & 0x3fff) + 1;
            }
            else if (type == "ANMF" && body.Length >= 16)
            {
                int delay = Read24(body[12..]);
                duration = checked(duration + delay); minimum = Math.Min(minimum, delay); frames++;
            }
            offset = checked(offset + 8 + (int)length + ((int)length & 1));
        }
        if (animated && frames == 0) throw new InvalidDataException("The animated WebP has no frames.");
        return Check(new("image/webp", ".webp", width, height, animated, duration, frames > 0 ? minimum : 0));
    }

    private static int Read24(ReadOnlySpan<byte> b) => b[0] | b[1] << 8 | b[2] << 16;
    private static MediaInfo Check(MediaInfo info) => info.Width is > 0 and <= 16384 && info.Height is > 0 and <= 16384
        ? info : throw new InvalidDataException("Image dimensions are invalid or too large.");
}
