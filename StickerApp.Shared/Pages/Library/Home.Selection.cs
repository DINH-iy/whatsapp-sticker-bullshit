using StickerApp.Core;

namespace StickerApp.Shared.Pages.Library;

public partial class Home
{
    private bool selectionMode;
    private IReadOnlyList<Sticker>? pendingBulkRemoval;

    private void ToggleSelectionMode()
    {
        selectionMode = !selectionMode;
        selected.Clear();
    }

    private Task ActivateImage(Sticker image)
    {
        if (busy) return Task.CompletedTask;
        if (!selectionMode) return Edit(image);
        Toggle(image.Id);
        return Task.CompletedTask;
    }

    private void SelectAllVisible()
    {
        if (!busy) selected.UnionWith(Visible.Select(image => image.Id));
    }

    private void RequestBulkRemoval()
    {
        if (busy || selected.Count == 0) return;
        // Keep the confirmation tied to these exact images, independent of
        // later filter changes or a preparation refresh.
        pendingBulkRemoval = library.Stickers.Where(image => selected.Contains(image.Id)).ToList();
    }
}
