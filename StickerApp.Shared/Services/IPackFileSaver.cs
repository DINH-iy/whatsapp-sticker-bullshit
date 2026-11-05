namespace StickerApp.Shared.Services;

public interface IPackFileSaver
{
    string ActionLabel { get; }
    Task SaveAsync(string fileName, Stream content, string contentType = "application/zip");
}
