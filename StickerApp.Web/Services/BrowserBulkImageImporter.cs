using StickerApp.Shared.Services;

namespace StickerApp.Web.Services;

public sealed class BrowserBulkImageImporter : IBulkImageImporter
{
    public bool IsAvailable => false;
    public string ActionLabel => "Import images";

    public Task<IReadOnlyList<BulkImageFile>> PickFolderAsync() =>
        throw new NotSupportedException("Folder import is available in the Android app.");
}
