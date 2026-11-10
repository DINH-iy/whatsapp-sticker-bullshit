using Microsoft.JSInterop;
namespace StickerApp.Shared.Pages.Library;

public partial class Home
{
    private bool showLayerEditor, compositionFromEditor, confirmLayeredText;
    private string? compositionStartWithMeme;
    private async Task AddMemeHeader(string placement) { await OpenEditedLayers(false); if (showLayerEditor) compositionStartWithMeme = placement; }
    private Guid? compositionStickerId;
    private bool compositionStartWithText,compositionStartWithShape,pendingLayeredShape;
    private bool? compositionInitialNsfw;
    private string? compositionInitialSource, compositionInitialName;
    private bool openingLayers;
    private string? layerOpeningDetail;
    private int layerOpeningVersion;

    private async Task CancelLayerOpening()
    {
        ++layerOpeningVersion;
        openingLayers = busy = false;
        if (gifSession is not null) await gifSession.InvokeVoidAsync("dispose");
    }
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
        compositionStartWithMeme = null;
        var version = ++layerOpeningVersion;
        try
        {
            busy = openingLayers = true;
            layerOpeningDetail = EditingAnimation && (HasTrim || transformed)
                ? "Applying your edits to each animation frame so text and shapes stay in place."
                : "Preparing your image and animation preview so text and shapes can be edited separately.";
            editError = null;
            StateHasChanged();
            await Task.Yield();
            string source;
            if (EditingAnimation && (HasTrim || transformed))
            {
                ValidateTrim();
                if (editRecipe is null) throw new InvalidOperationException("Wait for the animation editor to finish opening.");
                source = await gifSession!.InvokeAsync<string>("stage", TimeSpan.FromMinutes(4), editSource, trimStart, trimEnd, editRecipe.Value);
            }
            else source = EditingAnimation ? editSource : editPreview;
            if (version != layerOpeningVersion || editing is null || disposed) return;
            compositionStickerId = editing.Id;
            compositionInitialSource = source; compositionInitialName = editName;
            compositionStartWithText = startWithText;compositionStartWithShape=startWithShape;
            compositionInitialNsfw = editNsfw;
            compositionFromEditor = true;
            showLayerEditor = true;
        }
        catch (Exception exception)
        {
            if (version != layerOpeningVersion || disposed) return;
            Log.Error("Open edited layers", exception);
            editError = exception.Message.Split('\n')[0];
        }
        finally { if (version == layerOpeningVersion) busy = openingLayers = false; }
    }

    private void NewComposition()
    {
        compositionFromEditor = false; compositionStartWithMeme = null;
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
