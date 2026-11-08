namespace StickerApp.Core;

public sealed class LayerComposition
{
    public const int MaxLayers = 10;
    public const int MaxSourceCharacters = 24 * 1024 * 1024;
    public int Version { get; set; } = 1;
    public List<CompositionLayer> Layers { get; set; } = [];

    public void Validate()
    {
        if (Version != 1 || Layers is null || Layers.Count is < 1 or > MaxLayers)
            throw new InvalidDataException("A composition needs 1–10 layers.");
        long characters = 0;
        var ids = new HashSet<string>();
        foreach (var layer in Layers)
        {
            if (layer is null || string.IsNullOrEmpty(layer.Id) || layer.Id.Length > 64 || !ids.Add(layer.Id) ||
                string.IsNullOrWhiteSpace(layer.Name) || layer.Name.Length > 128 ||
                layer.Source is null || !layer.Source.StartsWith("data:image/png;base64,", StringComparison.Ordinal))
                throw new InvalidDataException("A layer has invalid image data or metadata.");
            layer.Text?.Validate();
            if (layer.Text is not null && layer.GifSource is not null) throw new InvalidDataException("A text layer cannot also be an animation source.");
            characters += layer.Source.Length + (layer.GifSource?.Length ?? 0) + (layer.Text?.FontData?.Length ?? 0);
            if (characters > MaxSourceCharacters) throw new InvalidDataException("Layer artwork and fonts exceed the 24 MB composition limit.");
            byte[] bytes;
            try { bytes = Convert.FromBase64String(layer.Source[22..]); }
            catch (FormatException) { throw new InvalidDataException("A layer image is damaged."); }
            var image = MediaInspector.Inspect(bytes);
            if (image.MimeType != "image/png" || image.Animated || image.Width > 1024 || image.Height > 1024)
                throw new InvalidDataException("Layer images must be still PNGs up to 1024 pixels per side.");
            if (layer.GifSource is not null)
            {
                if (!layer.GifSource.StartsWith("data:image/gif;base64,", StringComparison.Ordinal) && !layer.GifSource.StartsWith("data:image/webp;base64,", StringComparison.Ordinal)) throw new InvalidDataException("Invalid GIF layer source.");
                byte[] gif;
                try { gif = Convert.FromBase64String(layer.GifSource[(layer.GifSource.IndexOf(',') + 1)..]); }
                catch (FormatException) { throw new InvalidDataException("A GIF layer is damaged."); }
                var animation = MediaInspector.Inspect(gif);
                if (gif.Length > LibraryStore.MaxImportBytes || animation.MimeType is not ("image/gif" or "image/webp") || !animation.Animated ||
                    !Within(layer.GifDuration, 0.02, 600) || !Within(layer.TrimStart, 0, layer.GifDuration) ||
                    !Within(layer.TrimEnd, layer.TrimStart + 0.02, layer.GifDuration + 0.001) || layer.TrimEnd - layer.TrimStart > 10.001)
                    throw new InvalidDataException("Choose a valid GIF selection of at most 10 seconds.");
            }
            if (!Within(layer.X, -2048, 2560) || !Within(layer.Y, -2048, 2560) ||
                !Within(layer.Width, 1, 2048) || !Within(layer.Height, 1, 2048) ||
                !Within(layer.Rotation, -180, 180) || !Within(layer.Opacity, 0, 1) || !Within(layer.Hue, -180, 180) || !Within(layer.ColorStrength, 0, 1) || !Within(layer.BorderWidth, 0, 80) ||
                layer.BorderColor is null || layer.BorderColor.Length != 7 || layer.BorderColor[0] != '#' || !layer.BorderColor.AsSpan(1).ToString().All(Uri.IsHexDigit) ||
                (layer.Tint is not null && (layer.Tint.Length != 7 || layer.Tint[0] != '#' || !layer.Tint.AsSpan(1).ToString().All(Uri.IsHexDigit))))
                throw new InvalidDataException("A layer has invalid position, size, opacity, or color.");
        }
    }

    private static bool Within(double value, double min, double max) => double.IsFinite(value) && value >= min && value <= max;
}

public sealed class CompositionLayer
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Source { get; set; } = "";
    public TextLayerOptions? Text { get; set; }
    // Keep the serialized field name for existing projects; edited animations may be WebP.
    public string? GifSource { get; set; }
    public double GifDuration { get; set; }
    public double TrimStart { get; set; }
    public double TrimEnd { get; set; }
    // Position is the layer's center, in the 512 × 512 canvas coordinate space.
    public double X { get; set; } = 256;
    public double Y { get; set; } = 256;
    public double Width { get; set; } = 256;
    public double Height { get; set; } = 256;
    public double Rotation { get; set; }
    public double Opacity { get; set; } = 1;
    public bool FlipX { get; set; }
    public string? Tint { get; set; }
    public double Hue { get; set; }
    public double ColorStrength { get; set; } = 1;
    public double BorderWidth { get; set; }
    public string BorderColor { get; set; } = "#ffffff";
}
