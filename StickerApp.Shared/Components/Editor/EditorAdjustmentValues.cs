namespace StickerApp.Shared.Components.Editor;

// Shared values for the image editor and the selected composition layer.
public class EditorAdjustmentValues
{
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public double Rotation { get; set; }
    public double Opacity { get; set; } = 1;
    public string? Tint { get; set; }
}
