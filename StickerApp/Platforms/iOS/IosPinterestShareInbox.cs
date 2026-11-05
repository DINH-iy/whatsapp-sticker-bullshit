using Foundation;
using StickerApp.Core;
using StickerApp.Shared.Services;

namespace StickerApp;

public sealed class IosPinterestShareInbox : IPinterestShareInbox
{
    private static string? Container => NSFileManager.DefaultManager.GetContainerUrl(PinterestShareLink.AppGroup)?.Path;

    public PendingPinterestShare? Peek()
    {
        try
        {
            if (Container is not { } container) return null;
            var item = PinterestShareInboxFiles.Peek(container);
            return item is { } saved ? new(saved.Id, saved.Url) : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            System.Diagnostics.Debug.WriteLine($"Pinterest inbox: {exception.Message}");
            return null;
        }
    }

    public void Remove(string id)
    {
        try
        {
            if (Container is { } container) PinterestShareInboxFiles.Remove(container, id);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            System.Diagnostics.Debug.WriteLine($"Pinterest inbox: {exception.Message}");
        }
    }
}
