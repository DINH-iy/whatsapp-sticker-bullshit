namespace StickerApp.Shared.Services;

public interface INativeAppearance
{
    Task ApplyAsync(string background, bool dark);
}
