namespace StickerApp.Shared.Components.Editor.Layers;

// UI metadata and small previews only. Full source pixels stay in the canvas
// controller until Save exports the self-contained composition document.
public sealed class LayerEditorState
{
    public List<LayerPreview> Layers { get; set; } = [];
    public int Selected { get; set; } = -1;
    public bool CanUndo { get; set; }
    public bool CanRedo { get; set; }
}

public sealed class LayerPreview
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Thumbnail { get; set; } = "";
    public bool IsGif { get; set; }
    public double GifDuration { get; set; }
    public double TrimStart { get; set; }
    public double TrimEnd { get; set; }
    public List<string> GifThumbnails { get; set; } = [];
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public double Rotation { get; set; }
    public double Opacity { get; set; }
    public string? Tint { get; set; }
}
