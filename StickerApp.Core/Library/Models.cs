namespace StickerApp.Core;

public sealed class Sticker
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string FileName { get; set; } = "";
    public string? OriginalFileName { get; set; }
    public bool OriginalDiscarded { get; set; }
    public bool? KeepOriginalOverride { get; set; }
    public string? CompositionFileName { get; set; }
    public string MimeType { get; set; } = "";
    public int Width { get; set; }
    public int Height { get; set; }
    public long Bytes { get; set; }
    public bool Animated { get; set; }
    public int DurationMs { get; set; }
    public int MinimumFrameMs { get; set; }
    public bool Favorite { get; set; }
    public string Tags { get; set; } = "";
}

public sealed class StickerPack
{
    public string Publisher { get; set; } = "Sticker Studio";
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public bool Animated { get; set; }
    public string Version { get; set; } = Guid.NewGuid().ToString("N");
    public List<Guid> StickerIds { get; set; } = [];
    public string? CoverImage { get; set; }
}

public sealed class StickerLibrary
{
    public int SchemaVersion { get; set; } = 1;
    public List<Sticker> Stickers { get; set; } = [];
    public List<StickerPack> Packs { get; set; } = [];
    public List<CustomFont> Fonts { get; set; } = [];
}

public interface IWhatsAppExporter
{
    bool IsAvailable { get; }
    Task ExportAsync(StickerPack pack, IReadOnlyList<Sticker> stickers);
}

public sealed class BrowserWhatsAppExporter : IWhatsAppExporter
{
    public bool IsAvailable => false;
    public Task ExportAsync(StickerPack pack, IReadOnlyList<Sticker> stickers) =>
        throw new NotSupportedException("Open the Android or iPhone app to add packs to WhatsApp.");
}

