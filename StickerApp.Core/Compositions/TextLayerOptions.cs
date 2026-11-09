namespace StickerApp.Core;

public sealed class TextLayerOptions
{
    public string Content { get; set; } = "YOUR TEXT";
    public string Font { get; set; } = "meme";
    public string? FontData { get; set; }
    public double FontSize { get; set; } = 52;
    public double MaxWidth { get; set; } = 460;
    public bool Wrap { get; set; } = true;
    public double LineHeight { get; set; } = 1.15;
    public string Align { get; set; } = "center";
    public string Color { get; set; } = "#ffffff";
    public bool Gradient { get; set; }
    public string GradientColor { get; set; } = "#ff80c0";
    public double GradientAngle { get; set; } = 90;
    public string OutlineColor { get; set; } = "#000000";
    public double OutlineWidth { get; set; } = 3;
    public bool Bold { get; set; }
    public bool Italic { get; set; }
    public bool Underline { get; set; }
    public bool Strike { get; set; }
    public bool Uppercase { get; set; }
    public bool Shadow { get; set; }
    public string ShadowColor { get; set; } = "#000000";
    public bool Background { get; set; }
    public string BackgroundColor { get; set; } = "#000000";

    public void Validate()
    {
        static bool Range(double v, double min, double max) => double.IsFinite(v) && v >= min && v <= max;
        static bool ColorValid(string? c) => c is { Length: 7 } && c[0] == '#' && c.AsSpan(1).ToString().All(Uri.IsHexDigit);
        if (Content is null || Content.Length > 1000 || Font is null || Font.Length > 80 ||
            !(Font is "meme" or "sans" or "serif" or "mono" || (Font.StartsWith("custom-") && Guid.TryParseExact(Font[7..], "N", out _))) ||
            !Range(FontSize, 8, 256) || !Range(MaxWidth, 32, 1000) || !Range(LineHeight, 0.8, 3) ||
            !Range(GradientAngle, -180, 180) || !Range(OutlineWidth, 0, 16) || Align is not ("left" or "center" or "right") ||
            !ColorValid(Color) || !ColorValid(GradientColor) || !ColorValid(OutlineColor) || !ColorValid(ShadowColor) || !ColorValid(BackgroundColor))
            throw new InvalidDataException("Invalid text layer formatting.");
        if (FontData is not null)
        {
            if (!FontData.StartsWith("data:font/ttf;base64,", StringComparison.Ordinal) || FontData.Length > 6 * 1024 * 1024)
                throw new InvalidDataException("Invalid embedded font.");
            try { LibraryStore.ValidateFont(Convert.FromBase64String(FontData["data:font/ttf;base64,".Length..])); }
            catch (FormatException) { throw new InvalidDataException("Invalid embedded font data."); }
        }
        if (Font.StartsWith("custom-") && FontData is null) throw new InvalidDataException("The custom font is missing.");
    }
}
