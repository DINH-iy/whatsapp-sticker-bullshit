using System.Security.Cryptography;

namespace StickerApp.Core;

public sealed class CustomFont
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string FileName { get; set; } = "";
    public long Bytes { get; set; }
}
public sealed record EditorFont(string Id, string Name, string Data, long Bytes);

public sealed partial class LibraryStore
{
    public const int MaxFontBytes = 4 * 1024 * 1024;
    public static void ValidateFont(byte[] bytes)
    {
        if (bytes.Length is < 12 or > MaxFontBytes || !(bytes.AsSpan(0,4).SequenceEqual(new byte[]{0,1,0,0}) ||
            bytes.AsSpan(0,4).SequenceEqual("OTTO"u8) || bytes.AsSpan(0,4).SequenceEqual("wOFF"u8) || bytes.AsSpan(0,4).SequenceEqual("wOF2"u8)))
            throw new InvalidDataException("Choose a TTF, OTF, WOFF, or WOFF2 font up to 4 MB.");
    }
    public async Task<EditorFont> ImportFontAsync(string name, byte[] bytes)
    {
        ValidateFont(bytes); name = CleanName(Path.GetFileNameWithoutExtension(name));
        await gate.WaitAsync();
        try
        {
            var library = await ReadInternalAsync();
            var hash = SHA256.HashData(bytes);
            foreach(var existing in library.Fonts)
            {
                var previousHash = SHA256.HashData(await File.ReadAllBytesAsync(MediaPath(existing.FileName)));
                if (hash.AsSpan().SequenceEqual(previousHash)) return new(existing.Id, existing.Name, "data:font/ttf;base64," + Convert.ToBase64String(bytes), bytes.Length);
            }
            if (library.Fonts.Count >= 40) throw new InvalidOperationException("The font library supports up to 40 custom fonts.");
            var id = Guid.NewGuid().ToString("N"); var file = id + ".font";
            await File.WriteAllBytesAsync(MediaPath(file), bytes);
            library.Fonts.Add(new(){Id="custom-"+id,Name=name,FileName=file,Bytes=bytes.Length});
            try { await SaveLibrarySnapshotAsync(library); } catch { File.Delete(MediaPath(file)); throw; }
            return new("custom-"+id,name,"data:font/ttf;base64,"+Convert.ToBase64String(bytes),bytes.Length);
        }
        finally { gate.Release(); }
    }
    public async Task<EditorFont> ReadFontAsync(string id)
    {
        await gate.WaitAsync();
        try
        {
            var font = (await ReadInternalAsync()).Fonts.FirstOrDefault(f => f.Id == id) ?? throw new InvalidOperationException("This font no longer exists.");
            var bytes = await File.ReadAllBytesAsync(MediaPath(font.FileName)); ValidateFont(bytes);
            return new(font.Id,font.Name,"data:font/ttf;base64,"+Convert.ToBase64String(bytes),bytes.Length);
        }
        finally { gate.Release(); }
    }
    public async Task<List<EditorFont>> ReadFontsAsync()
    {
        await gate.WaitAsync();
        try
        {
            var fonts = new List<EditorFont>();
            foreach(var font in (await ReadInternalAsync()).Fonts)
            {
                var bytes=await File.ReadAllBytesAsync(MediaPath(font.FileName)); ValidateFont(bytes);
                fonts.Add(new(font.Id,font.Name,"data:font/ttf;base64,"+Convert.ToBase64String(bytes),bytes.Length));
            }
            return fonts;
        }
        finally { gate.Release(); }
    }
}
