using System.Buffers.Binary;
using System.Text;

namespace StickerApp.Core;

public sealed record MediaInfo(string MimeType, string Extension, int Width, int Height, bool Animated, int DurationMs = 0, int MinimumFrameMs = 0);

public static class MediaInspector
{
    public static byte[] AsSingleFrameAnimation(ReadOnlySpan<byte> data, int durationMs = 1000)
    {
        var info = Inspect(data);
        if (info.MimeType != "image/webp" || info.Animated)
            return data.ToArray();
        if (durationMs < 8 || durationMs > 10_000)
            throw new ArgumentOutOfRangeException(nameof(durationMs));

        List<byte[]> frameChunks = [];
        for (var offset = 12; offset + 8 <= data.Length;)
        {
            var length = BinaryPrimitives.ReadUInt32LittleEndian(data[(offset + 4)..]);
            if (length > int.MaxValue || offset + 8L + length > data.Length)
                throw new InvalidDataException("Invalid WebP chunk.");
            var type = Encoding.ASCII.GetString(data.Slice(offset, 4));
            if (type is "ALPH" or "VP8 " or "VP8L")
            {
                var chunkLength = checked(8 + (int)length + ((int)length & 1));
                frameChunks.Add(data.Slice(offset, chunkLength).ToArray());
            }
            offset = checked(offset + 8 + (int)length + ((int)length & 1));
        }
        if (frameChunks.Count == 0)
            throw new InvalidDataException("The WebP contains no image frame.");

        var vp8x = new byte[10];
        vp8x[0] = 0x12;
        Write24(vp8x, 4, info.Width - 1);
        Write24(vp8x, 7, info.Height - 1);
        var frameHeader = new byte[16];
        Write24(frameHeader, 6, info.Width - 1);
        Write24(frameHeader, 9, info.Height - 1);
        Write24(frameHeader, 12, durationMs);
        frameHeader[15] = 2;

        var body = Join(Chunk("VP8X", vp8x), Chunk("ANIM", new byte[6]),
            Chunk("ANMF", Join(frameHeader, Join(frameChunks.ToArray()))));
        using var output = new MemoryStream(8 + body.Length);
        output.Write("RIFF"u8);
        WriteUInt32(output, checked((uint)(body.Length + 4)));
        output.Write("WEBP"u8);
        output.Write(body);
        return output.ToArray();
    }

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
    private static void Write24(byte[] bytes, int offset, int value)
    {
        bytes[offset] = (byte)value;
        bytes[offset + 1] = (byte)(value >> 8);
        bytes[offset + 2] = (byte)(value >> 16);
    }

    private static byte[] Chunk(string name, ReadOnlySpan<byte> body)
    {
        var output = new byte[8 + body.Length + (body.Length & 1)];
        Encoding.ASCII.GetBytes(name, output);
        BinaryPrimitives.WriteUInt32LittleEndian(output.AsSpan(4), (uint)body.Length);
        body.CopyTo(output.AsSpan(8));
        return output;
    }

    private static byte[] Join(params byte[][] parts)
    {
        var output = new byte[parts.Sum(part => part.Length)];
        var offset = 0;
        foreach (var part in parts)
        {
            part.CopyTo(output, offset);
            offset += part.Length;
        }
        return output;
    }

    private static void WriteUInt32(Stream output, uint value)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, value);
        output.Write(bytes);
    }

    private static MediaInfo Check(MediaInfo info) => info.Width is > 0 and <= 16384 && info.Height is > 0 and <= 16384
        ? info : throw new InvalidDataException("Image dimensions are invalid or too large.");
}
