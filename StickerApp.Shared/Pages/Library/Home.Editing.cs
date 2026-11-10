using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.JSInterop;
using StickerApp.Core;

namespace StickerApp.Shared.Pages.Library;

public partial class Home
{
    private IDisposable? preparationLease;
    private bool editorToolActive;
    protected override async Task OnAfterRenderInteropAsync(bool firstRender)
    {
        if (editing is null || IsDisposed) return;
        editDialogHelper ??= await JS.InvokeAsync<IJSObjectReference>("import", "./_content/StickerApp.Shared/js/ui/dialog.js");
        if (IsDisposed || editing is null) return;
        await editDialogHelper.InvokeVoidAsync("open", editDialog);
    }

    private Task EditorKeyDown(Microsoft.AspNetCore.Components.Web.KeyboardEventArgs args) =>
        args.Key == "Escape" ? CancelEditing() : Task.CompletedTask;

    private void EditorToolsReady()
    {
        editorToolsLoading = false;
        if (!editorLoading) busyText = "Processing…";
    }

    private int editorSession;
    private async Task CancelEditing()
    {
        if ((busy && !editorLoading && !openingLayers) || (customizing && !editorToolsLoading)) return;
        if (openingLayers) await CancelLayerOpening();
        ++editorSession; // Late file reads or GIF results belong to the closed editor.
        editing = null;
        preparationLease?.Dispose(); preparationLease = null;
        editorToolsLoading = customizing = false;
        if (gifSession is not null) await gifSession.InvokeVoidAsync("dispose");
        busy = editorLoading = false;
        busyText = "Processing…";
    }

    private async Task Edit(Sticker image)
    {
        if (busy || IsPreparing(image)) return;
        using var performance = Log.Measure("Editor: open image", $"animated={image.Animated}; bytes={image.Bytes}; pixels={image.Width}x{image.Height}");
        if (image.CompositionFileName is not null)
        {
            compositionFromEditor = false; compositionStartWithMeme = null;
            compositionStickerId = image.Id;
            compositionStartWithText = false; compositionInitialSource = compositionInitialName = null; compositionInitialNsfw = null;
            showLayerEditor = true;
            return;
        }
        editorToolActive = false;
        var session = ++editorSession;
        busy = editorLoading = editorToolsLoading = true;
        busyText = image.Animated ? "Loading animation…" : "Loading image…";
        editing = image;
        keepEditOriginal = image.KeepOriginalOverride ?? (image.Animated ? Store.KeepOriginalAnimations : Store.KeepOriginalStillImages);
        editName = image.Name;
        editNsfw = image.Nsfw;
        editSource = editPreview = previews[image.Id];
        replacement = null;
        transformed = false;
        editRecipe = null;
        replacementAnimated = false;
        editError = null;
        gifDuration = trimStart = trimEnd = 0;
        trimmedPreview = null;
        gifThumbnails = [];
        StateHasChanged();
        await Task.Yield();
        try
        {
            IDisposable lease;
            using (Log.Measure("Editor: wait for preparation to stop")) lease = await Preparation.SuspendForEditingAsync();
            if (session != editorSession || disposed) { lease.Dispose(); return; }
            preparationLease?.Dispose(); preparationLease = lease;
            // Preparation may have completed just before the lease was acquired.
            var current = (await Store.ReadAsync()).Stickers.FirstOrDefault(item => item.Id == image.Id)
                ?? throw new InvalidOperationException("This image is no longer available.");
            string source;
            using (Log.Measure("Editor: read image source")) source = await Store.PreviewAsync(current);
            if (session != editorSession || disposed) return;
            editing = current;
            editSource = editPreview = source;
            StateHasChanged();
            // Edit the current animation, including saved cutouts. The retained
            // original is a backup and must not silently replace the edited sticker.
            if (session != editorSession || disposed) return;
            if (EditingAnimation) await InspectGif(session);
        }
        catch (Exception exception)
        {
            if (session != editorSession || disposed) return;
            Log.Error("GIF inspection", exception);
            editError = "Could not read this GIF, or it is too large to edit.";
        }
        finally
        {
            if (session == editorSession && !disposed)
            {
                busy = editorLoading = false;
                if (!editorToolsLoading) busyText = "Processing…";
            }
        }
    }

    private async Task Replace(InputFileChangeEventArgs e)
    {
        if (busy)
            return;
        var session = ++editorSession;
        busy = editorLoading = editorToolsLoading = true;
        busyText = "Loading image…";
        editError = null;
        try
        {
            var bytes = await ReadFile(e.File);
            if (session != editorSession || disposed) return;
            var info = MediaInspector.Inspect(bytes);
            replacement = bytes;
            replacementAnimated = info.Animated;
            transformed = false;
        editRecipe = null;
            editPreview = editSource = $"data:{info.MimeType};base64,{Convert.ToBase64String(bytes)}";
            gifDuration = trimStart = trimEnd = 0;
            trimmedPreview = null;
            gifThumbnails = [];
            if (EditingAnimation)
                await InspectGif(session);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException or JSException)
        {
            if (session != editorSession || disposed) return;
            editorToolsLoading = false;
            Log.Error("Image editing", ex);
            editError = ex is JSException ? "Could not read this GIF, or it is too large to edit." : ex.Message;
        }
        finally
        {
            if (session == editorSession && !disposed)
            {
                busy = editorLoading = false;
                if (!editorToolsLoading) busyText = "Processing…";
            }
        }
    }

    private void CustomizeImage(string png)
    {
        editPreview = png;
        transformed = true;
        trimmedPreview = null;
        editError = null;
    }

    private async Task InspectGif(int session)
    {
        using var performance = Log.Measure("Editor: inspect animation and timeline");
        gifEditor ??= await JS.InvokeAsync<IJSObjectReference>("import", "./_content/StickerApp.Shared/js/media/animation/gif-editor.js");
        if (session != editorSession || disposed) return;
        gifSession ??= await gifEditor.InvokeAsync<IJSObjectReference>("createSession");
        if (session != editorSession || disposed) return;
        var info = await gifSession.InvokeAsync<GifInfo>("inspect", editSource);
        if (session != editorSession || disposed) return;
        gifDuration = trimEnd = info.Duration;
        trimStart = 0;
        gifThumbnails = info.Thumbnails;
    }

    private void ChangeEditorTrimStart(double value) { trimStart = value; TrimStartChanged(); }
    private void ChangeEditorTrimEnd(double value) { trimEnd = value; TrimEndChanged(); }

    private void TrimStartChanged()
    {
        trimStart = Math.Clamp(trimStart, 0, Math.Max(0, trimEnd - 0.01));
        ResetTrimPreview();
    }

    private void TrimEndChanged()
    {
        trimEnd = Math.Clamp(trimEnd, Math.Min(gifDuration, trimStart + 0.01), gifDuration);
        ResetTrimPreview();
    }

    private void ResetTrimPreview()
    {
        trimmedPreview = null;
        editPreview = editSource;
        editError = null;
    }

    private void ResetTrim()
    {
        trimStart = 0;
        trimEnd = gifDuration;
        ResetTrimPreview();
    }

    private void ValidateTrim()
    {
        if (!double.IsFinite(trimStart) || !double.IsFinite(trimEnd) || trimStart < 0 || trimEnd > gifDuration || trimEnd - trimStart < 0.01)
            throw new InvalidOperationException("Start must be before end, within the GIF duration.");
    }
    private StickerApp.Shared.Services.StickerPreparationService.Progress? animationProgress;
    [JSInvokable] public Task AnimationProgress(StickerApp.Shared.Services.StickerPreparationService.Progress value) => InvokeAsync(() => { if (IsDisposed) return; animationProgress = value; StateHasChanged(); });
    private async Task<string> BuildTrim()
    {
        ValidateTrim();
        if (editRecipe is null) throw new InvalidOperationException("Wait for the animation editor to finish opening.");
        using var reference = DotNetObjectReference.Create(this);
        return trimmedPreview ??= await gifSession!.InvokeAsync<string>("edit", TimeSpan.FromMinutes(4), editSource, trimStart, trimEnd, editRecipe.Value, reference);
    }

    private bool savingEditor;
    private Task Save() => SaveEdited(false);
    private Task SaveCopy() => SaveEdited(true);
    private async Task SaveEdited(bool copy)
    {
        if (busy || customizing || editing is null)
            return;
        busy = savingEditor = true; animationProgress = null;
        busyText = "Saving stickers…";
        editError = null;
        try
        {
            if (string.IsNullOrWhiteSpace(editName) || editName.Trim().Length > 128)
                throw new InvalidOperationException("Name: 1–128 characters.");
            var bytes = transformed ? Convert.FromBase64String(editPreview[(editPreview.IndexOf(',') + 1)..]) : replacement;
            if (EditingAnimation && (HasTrim || transformed))
            {
                busyText = "Applying animation edits…";
                var gif = await BuildTrim();
                bytes = Convert.FromBase64String(gif[(gif.IndexOf(',') + 1)..]);
            }
            var savedId = editing.Id;
            if (copy)
            {
                bytes ??= Convert.FromBase64String(editSource[(editSource.IndexOf(',') + 1)..]);
                savedId = await Store.ImportAsync(editName, bytes, activePack, keepEditOriginal);
                await Store.MoveToFolderAsync([savedId], editing.FolderId);
            }
            else if (bytes is not null)
            {
                if (!await Store.ReplaceImageAsync(editing.Id, bytes, editing.FileName, keepOriginal: keepEditOriginal))
                    throw new InvalidOperationException("This image changed while you were editing. Reopen it before saving.");
            }
            await Store.EditStickerAsync(savedId, editName, editing.Tags, editNsfw);
            await Reload();
            editing = null;
            preparationLease?.Dispose(); preparationLease = null;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException or JSException)
        {
            Log.Error("Image editing", ex);
            editError = ex is JSException ? "Could not save this animation. Try a shorter selection or a smaller animation." : ex.Message;
        }
        finally
        {
            busy = savingEditor = false;
            busyText = "Processing…";
        }
    }
}
