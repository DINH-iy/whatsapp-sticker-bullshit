using Microsoft.JSInterop;
namespace StickerApp.Shared.Pages.Library;

public partial class Home
{
    private bool showLayerEditor, compositionFromEditor, confirmLayeredText;
    private Guid? compositionStickerId;
    private bool compositionStartWithText,compositionStartWithShape,pendingLayeredShape;
    private bool? compositionInitialNsfw;
    private string? compositionInitialSource, compositionInitialName;
    private async Task RequestAddText() {
        if(busy || customizing)return;
        pendingLayeredShape=false;
        await using var preferences=await JS.InvokeAsync<Microsoft.JSInterop.IJSObjectReference>("import","./_content/StickerApp.Shared/js/ui/layer-preferences.js");
        if(await preferences.InvokeAsync<bool>("shouldWarn"))confirmLayeredText=true;
        else await OpenEditedLayers(startWithText:true);
    }
    private async Task RequestAddShape(){
        if(busy || customizing)return;
        pendingLayeredShape=true;
        await using var preferences=await JS.InvokeAsync<IJSObjectReference>("import","./_content/StickerApp.Shared/js/ui/layer-preferences.js");
        if(await preferences.InvokeAsync<bool>("shouldWarn"))confirmLayeredText=true;
        else await OpenEditedLayers(false,true);
    }
    private async Task ConfirmAddText() { confirmLayeredText = false; await OpenEditedLayers(!pendingLayeredShape,pendingLayeredShape); }
    private void CancelComposition() { showLayerEditor = false; compositionFromEditor = false; }
    private Task MakeEditedLayered() => OpenEditedLayers(startWithText: false);
    private async Task OpenEditedLayers(bool startWithText,bool startWithShape=false)
    {
        if (busy || customizing || editing is null) return;
        try
        {
            busy = true;
            var source = EditingAnimation && (HasTrim || transformed) ? await BuildTrim() : EditingAnimation ? editSource : editPreview;
            compositionStickerId = editing.Id;
            compositionInitialSource = source; compositionInitialName = editName;
            compositionStartWithText = startWithText;compositionStartWithShape=startWithShape;
            compositionInitialNsfw = editNsfw;
            compositionFromEditor = true;
            showLayerEditor = true;
        }
        catch (Exception exception) when (exception is Microsoft.JSInterop.JSException or InvalidOperationException or IOException)
        { editError = exception.Message.Split('\n')[0]; }
        finally { busy = false; }
    }

    private void NewComposition()
    {
        compositionFromEditor = false;
        compositionStickerId = null;
        compositionStartWithText = compositionStartWithShape = false; compositionInitialSource = compositionInitialName = null; compositionInitialNsfw = null;
        showLayerEditor = true;
    }

    private async Task CompositionSaved(Guid id)
    {
        showLayerEditor = false;
        if (compositionFromEditor) { compositionFromEditor = false; await CancelEditing(); }
        await Reload();
        notice = "Layered sticker saved. Tap its pencil button to edit the layers again.";
        noticeIsError = false;
    }
}
