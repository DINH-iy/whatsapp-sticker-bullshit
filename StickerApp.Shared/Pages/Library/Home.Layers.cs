namespace StickerApp.Shared.Pages.Library;

public partial class Home
{
    private bool showLayerEditor;
    private Guid? compositionStickerId;
    private bool compositionStartWithText;
    private bool? compositionInitialNsfw;
    private string? compositionInitialSource, compositionInitialName;
    private Task AddTextToEdited() => OpenEditedLayers(startWithText: true);
    private Task MakeEditedLayered() => OpenEditedLayers(startWithText: false);
    private async Task OpenEditedLayers(bool startWithText)
    {
        if (busy || customizing || editing is null) return;
        try
        {
            busy = true;
            var source = EditingAnimation && (HasTrim || transformed) ? await BuildTrim() : EditingAnimation ? editSource : editPreview;
            compositionStickerId = editing.Id;
            compositionInitialSource = source; compositionInitialName = editName;
            compositionStartWithText = startWithText;
            compositionInitialNsfw = editNsfw;
            busy = false; await CancelEditing();
            showLayerEditor = true;
        }
        catch (Exception exception) when (exception is Microsoft.JSInterop.JSException or InvalidOperationException or IOException)
        { editError = exception.Message.Split('\n')[0]; }
        finally { busy = false; }
    }

    private void NewComposition()
    {
        compositionStickerId = null;
        compositionStartWithText = false; compositionInitialSource = compositionInitialName = null; compositionInitialNsfw = null;
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
