namespace StickerApp.Shared.Pages.Library;

public partial class Home
{
    private bool showLayerEditor;
    private Guid? compositionStickerId;

    private void NewComposition()
    {
        compositionStickerId = null;
        showLayerEditor = true;
    }

    private async Task CompositionSaved(Guid id)
    {
        showLayerEditor = false;
        await Reload();
        notice = "Layered sticker saved. Tap its pencil button to edit the layers again.";
        noticeIsError = false;
    }
}
