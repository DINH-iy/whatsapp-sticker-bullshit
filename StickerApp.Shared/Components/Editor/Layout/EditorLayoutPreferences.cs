namespace StickerApp.Shared.Components.Editor;

public sealed record EditorLayoutPreferences
{
    public string ToolsPosition { get; set; } = "bottom";
    public string ControlsPosition { get; set; } = "bottom";
    public string ButtonSize { get; set; } = "comfortable";
    public bool ShowTimeline { get; set; } = true;
    public string[] Categories { get; set; } = ["cutout", "draw", "text", "adjust", "layers"];
}
