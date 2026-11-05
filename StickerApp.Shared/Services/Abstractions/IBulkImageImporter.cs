namespace StickerApp.Shared.Services;

public sealed record BulkImageFile(string Name, byte[] Bytes);

public interface IBulkImageImporter
{
    bool IsAvailable { get; }
    string ActionLabel { get; }
    Task<IReadOnlyList<BulkImageFile>> PickFolderAsync();
}
