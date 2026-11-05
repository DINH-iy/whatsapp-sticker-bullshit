namespace StickerApp.Shared.Components.Library;

public sealed class OriginalImportChoice(string name, long bytes, bool animated, bool keepOriginal)
{
    public string Name { get; } = name;
    public bool Animated { get; } = animated;
    public bool KeepOriginal { get; set; } = keepOriginal;
    public string SizeLabel => bytes < 1_000_000 ? $"{bytes / 1000d:N1} KB source" : $"{bytes / 1_000_000d:N1} MB source";
}
