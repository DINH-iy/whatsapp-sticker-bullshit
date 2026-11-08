namespace StickerApp.Core;

public sealed class DiscordFavorite
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "Discord favourite";
    public string PageUrl { get; set; } = "";
    public string MediaUrl { get; set; } = "";
    public bool Video { get; set; }
    public double Order { get; set; }
    public string? Problem { get; set; }
}
public sealed class DiscordExportSelection
{
    public List<DiscordFavorite> Items { get; set; } = [];
    public int Skipped { get; set; }
    public int DuplicateCount { get; set; }
}
public sealed record DiscordImportProgress(int Completed, int Total, bool ReviewingVideo = false, bool Saving = false);
public sealed record DiscordMediaDownload(byte[] Bytes, bool Video);
