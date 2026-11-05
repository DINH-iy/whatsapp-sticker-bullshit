using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.JSInterop;
using StickerApp.Core;

namespace StickerApp.Shared.Pages.Library;

public partial class Home
{
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (editing is null) return;
        editDialogHelper ??= await JS.InvokeAsync<IJSObjectReference>("import", "./_content/StickerApp.Shared/js/ui/dialog.js");
        await editDialogHelper.InvokeVoidAsync("open", editDialog);
    }

    private void EditorKeyDown(Microsoft.AspNetCore.Components.Web.KeyboardEventArgs args)
    {
        if (args.Key == "Escape" && !busy && !customizing) editing = null;
    }

    private async Task Edit(Sticker image)
    {
        if (busy) return;
        if (image.CompositionFileName is not null)
        {
            compositionStickerId = image.Id;
            showLayerEditor = true;
            return;
        }
        busy = editorLoading = true;
        busyText = image.Animated ? "Loading animation…" : "Loading image…";
        editing = image;
        editName = image.Name;
        editSource = editPreview = previews[image.Id];
        replacement = null;
        transformed = false;
        replacementAnimated = false;
        editError = null;
        gifDuration = trimStart = trimEnd = 0;
        trimmedPreview = null;
        gifThumbnails = [];
        StateHasChanged();
        await Task.Yield();
        try
        {
            // Keep the imported GIF available for trimming after automatic conversion.
            if (image.Animated && image.OriginalFileName is { } original)
            {
                try
                {
                    var originalBytes = await File.ReadAllBytesAsync(Store.MediaPath(original));
                    if (MediaInspector.Inspect(originalBytes).MimeType == "image/gif")
                        editSource = editPreview = $"data:image/gif;base64,{Convert.ToBase64String(originalBytes)}";
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
            }
            if (IsGif) await InspectGif();
        }
        catch (JSException exception)
        {
            Log.Error("GIF inspection", exception);
            editError = "Could not read this GIF, or it is too large to edit.";
        }
        finally
        {
            busy = editorLoading = false;
            busyText = "Processing…";
        }
    }

    private async Task Replace(InputFileChangeEventArgs e)
    {
        if (busy)
            return;
        busy = editorLoading = true;
        busyText = "Loading image…";
        editError = null;
        try
        {
            var bytes = await ReadFile(e.File);
            var info = MediaInspector.Inspect(bytes);
            replacement = bytes;
            replacementAnimated = info.Animated;
            transformed = false;
            editPreview = editSource = $"data:{info.MimeType};base64,{Convert.ToBase64String(bytes)}";
            gifDuration = trimStart = trimEnd = 0;
            trimmedPreview = null;
            gifThumbnails = [];
            if (IsGif)
                await InspectGif();
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException or JSException)
        {
            Log.Error("Image editing", ex);
            editError = ex is JSException ? "Could not read this GIF, or it is too large to edit." : ex.Message;
        }
        finally
        {
            busy = editorLoading = false;
            busyText = "Processing…";
        }
    }

    private void CustomizeImage(string png)
    {
        editPreview = png;
        transformed = true;
        editError = null;
    }

    private async Task InspectGif()
    {
        gifEditor ??= await JS.InvokeAsync<IJSObjectReference>("import", "./_content/StickerApp.Shared/js/media/gif-editor.js");
        var info = await gifEditor.InvokeAsync<GifInfo>("inspect", editSource);
        gifDuration = trimEnd = info.Duration;
        trimStart = 0;
        gifThumbnails = info.Thumbnails;
    }

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
    private async Task<string> BuildTrim()
    {
        ValidateTrim();
        return trimmedPreview ??= await gifEditor!.InvokeAsync<string>("trim", editSource, trimStart, trimEnd);
    }

    private async Task PreviewTrim()
    {
        if (busy)
            return;
        busy = true;
        busyText = "Trimming…";
        editError = null;
        try
        {
            editPreview = await BuildTrim();
        }
        catch (Exception ex) when (ex is JSException or InvalidOperationException)
        {
            Log.Error("Image editing", ex);
            editError = ex is JSException ? "Could not trim this GIF. Try a shorter selection or a smaller GIF." : ex.Message;
        }
        finally
        {
            busy = false;
            busyText = "Processing…";
        }
    }

    private async Task Save()
    {
        if (busy || customizing || editing is null)
            return;
        busy = true;
        editError = null;
        try
        {
            if (string.IsNullOrWhiteSpace(editName) || editName.Trim().Length > 128)
                throw new InvalidOperationException("Name: 1–128 characters.");
            var bytes = transformed ? Convert.FromBase64String(editPreview[(editPreview.IndexOf(',') + 1)..]) : replacement;
            if (HasTrim)
            {
                busyText = "Trimming…";
                var gif = await BuildTrim();
                bytes = Convert.FromBase64String(gif[(gif.IndexOf(',') + 1)..]);
            }
            if (bytes is not null)
                await Store.ReplaceImageAsync(editing.Id, bytes);
            await Store.EditStickerAsync(editing.Id, editName, editing.Tags);
            await Reload();
            editing = null;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException or JSException)
        {
            Log.Error("Image editing", ex);
            editError = ex is JSException ? "Could not trim this GIF. Try a shorter selection or a smaller GIF." : ex.Message;
        }
        finally
        {
            busy = false;
            busyText = "Processing…";
        }
    }
}
