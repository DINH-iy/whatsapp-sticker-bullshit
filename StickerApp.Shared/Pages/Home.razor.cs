using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.JSInterop;
using StickerApp.Core;

namespace StickerApp.Shared.Pages;

public partial class Home
{
    private StickerLibrary library = new();
    private Dictionary<Guid, string> previews = [];
    private readonly HashSet<Guid> selected = [];
    [Parameter] public Guid? PackId { get; set; }
    private Guid? activePack;
    // Library filters and pack creation.
    private string search = "";
    private string destination = "";
    private string packName = "";

    // The editor keeps the source separate from its preview so Reset can restore it.
    private string editName = "";
    private string editPreview = "";
    private string editSource = "";
    private bool busy;
    private bool renamingPack;
    private bool showGifSearch;
    private bool disposed;
    private bool preparationRefreshPending;
    private Guid? infoImageId;
    private Sticker? InfoImage => library.Stickers.FirstOrDefault(image => image.Id == infoImageId);
    private IEnumerable<Sticker> PreparationImages => CurrentPack is null ? library.Stickers : PackImages;
    private int PendingPreparationCount => PreparationImages.Count(image => StickerApp.Shared.Services.StickerPreparationService.NeedsPreparation(image) && Preparation.GetStatus(image)?.Error is null);
    private int AttentionCount => PreparationImages.Count(image => Preparation.GetStatus(image)?.Error is not null);
    private bool deletePack;
    private ElementReference editDialog;
    private IJSObjectReference? editDialogHelper;
    private bool customizing;
    private bool EditingStill => editing is not null && (replacement is not null ? !replacementAnimated : !editing.Animated);
    private bool replacementAnimated;
    private bool transformed;
    private string? notice;
    private bool noticeIsError;
    private string? editError;
    private Sticker? editing;
    private Sticker? pendingRemoval;
    private Sticker? imageToAdd;
    private StickerPack? packToShare;
    private byte[]? replacement;
    private IJSObjectReference? gifEditor;
    private string busyText = "Processing…";
    private double gifDuration;
    private double trimStart;
    private double trimEnd;
    private string? trimmedPreview;
    private List<string> gifThumbnails = [];
    private bool IsGif => editSource.StartsWith("data:image/gif;base64,", StringComparison.Ordinal);
    private string GifMax => gifDuration.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
    private bool HasTrim => IsGif && gifDuration > 0 && (trimStart != 0 || trimEnd != gifDuration);
    private string TrimLeft => (trimStart / gifDuration * 100).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
    private string TrimRight => ((gifDuration - trimEnd) / gifDuration * 100).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
    private sealed class GifInfo
    {
        public double Duration { get; set; }
        public int FrameCount { get; set; }
        public List<string> Thumbnails { get; set; } = [];
    }
    private StickerPack? CurrentPack => library.Packs.FirstOrDefault(p => p.Id == activePack);
    private IReadOnlyList<Sticker> PackImages => CurrentPack is { } pack
        ? pack.StickerIds
            .Select(id => library.Stickers.FirstOrDefault(sticker => sticker.Id == id))
            .OfType<Sticker>()
            .ToList()
        : [];
    private string? PackHeaderCover => CurrentPack?.CoverImage ?? (PackImages.FirstOrDefault() is { } first && previews.TryGetValue(first.Id, out var preview) ? preview : null);
    private IReadOnlyList<PackRules.Issue> Issues => CurrentPack is { } p ? PackRules.Inspect(p, PackImages) : [];
    private IReadOnlyList<string> Validation => CurrentPack is { } p ? PackRules.Validate(p, PackImages) : [];
    private IEnumerable<Sticker> Visible => (activePack is null
        ? library.Stickers.AsEnumerable().Reverse()
        : PackImages)
        .Where(sticker => sticker.Name.Contains(search, StringComparison.OrdinalIgnoreCase));

    protected override void OnInitialized() => Preparation.Changed += OnPreparationChanged;

    private void OnPreparationChanged(bool mediaChanged) => _ = InvokeAsync(async () =>
    {
        if (disposed) return;
        if (mediaChanged)
        {
            if (busy) preparationRefreshPending = true;
            else await Reload();
        }
        if (!disposed) StateHasChanged();
    });

    private async Task EditFromInfo()
    {
        var image = InfoImage;
        infoImageId = null;
        if (image is not null) await Edit(image);
    }

    protected override Task OnParametersSetAsync() => Run(() =>
    {
        activePack = PackId;
        selected.Clear();
        renamingPack = false;
        showGifSearch = false;
        infoImageId = null;
        return Task.CompletedTask;
    });

    private async Task Reload()
    {
        var updatedLibrary = await Store.ReadAsync();
        var updatedPreviews = new Dictionary<Guid, string>();
        foreach (var image in updatedLibrary.Stickers)
            updatedPreviews[image.Id] = await Store.PreviewAsync(image);

        // Publish a complete snapshot so navigation cannot render images without previews.
        library = updatedLibrary;
        previews = updatedPreviews;
        selected.IntersectWith(library.Stickers.Select(s => s.Id));
    }

    private async Task Run(Func<Task> action, [System.Runtime.CompilerServices.CallerMemberName] string operation = "Sticker action")
    {
        if (busy)
            return;
        busy = true;
        Log.Info(operation, "Started.");
        notice = null;
        noticeIsError = false;
        try
        {
            await action();
            await Reload();
            Log.Info(operation, "Completed.");
        }
        catch (Exception e) when (e is IOException or InvalidDataException or InvalidOperationException or NotSupportedException or JSException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            var reference = Log.Error(operation, e);
            notice = (e is JSException ? e.Message.Split('\n')[0] : e.Message) + $" Reference: {reference}.";
            noticeIsError = true;
        }
        finally
        {
            busy = false;
            if (preparationRefreshPending && !disposed)
            {
                preparationRefreshPending = false;
                await Reload();
            }
        }
    }
    private static async Task<byte[]> ReadFile(IBrowserFile file)
    {
        await using var stream = file.OpenReadStream(LibraryStore.MaxImportBytes);
        using var memory = new MemoryStream();
        await stream.CopyToAsync(memory);
        return memory.ToArray();
    }

    private Task Import(InputFileChangeEventArgs e) => Run(async () =>
    {
        // Capture the destination before reading files so navigation cannot change it mid-import.
        var packId = activePack;
        List<string> errors = [];
        foreach (var file in e.GetMultipleFiles(30))
        {
            try
            {
                await Store.ImportAsync(file.Name, await ReadFile(file), packId);
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException) { Log.Error("Image import", ex); errors.Add($"{file.Name}: {ex.Message}"); }
        }
        if (errors.Count > 0)
        {
            notice = string.Join(" ", errors);
            noticeIsError = true;
        }
    });

    private Task ImportFolder() => Run(async () =>
    {
        var files = await BulkImporter.PickFolderAsync();
        var packId = activePack;
        var imported = 0;
        var errors = new List<string>();
        foreach (var file in files.Take(30))
        {
            try
            {
                await Store.ImportAsync(file.Name, file.Bytes, packId);
                imported++;
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException)
            {
                Log.Error("Folder import", ex);
                errors.Add($"{file.Name}: {ex.Message}");
            }
        }

        if (files.Count > 30)
            errors.Insert(0, $"Only the first 30 supported images were imported; {files.Count - 30} were skipped.");
        if (imported > 0)
            notice = $"Imported {imported} image{(imported == 1 ? "" : "s")} from the folder.";
        if (errors.Count > 0)
        {
            notice = notice is null ? string.Join(" ", errors) : $"{notice} {string.Join(" ", errors)}";
            noticeIsError = true;
        }
    });

    private Task ImportPack(InputFileChangeEventArgs args) => Run(async () =>
    {
        busyText = "Importing pack…";
        try
        {
            await using var input = args.File.OpenReadStream(PackArchive.MaxArchiveBytes);
            activePack = await Store.ImportPackAsync(input);
            var updatedLibrary = await Store.ReadAsync();
            packName = updatedLibrary.Packs.Single(pack => pack.Id == activePack).Name;
            selected.Clear();
            Navigation.NavigateTo($"packs/{activePack}");
        }
        finally
        {
            busyText = "Processing…";
        }
    });

    private async Task FinishQuickAdd(string message)
    {
        imageToAdd = null;
        await Run(() =>
        {
            notice = message;
            return Task.CompletedTask;
        });
    }

    private void BeginRenamePack()
    {
        packName = CurrentPack?.Name ?? "";
        renamingPack = true;
    }

    private Task RenamePack() => Run(async () =>
    {
        await Store.RenamePackAsync(activePack!.Value, packName);
        renamingPack = false;
    });

    private void Toggle(Guid id)
    {
        if (!selected.Add(id))
            selected.Remove(id);
    }

    private Task CopySelection() => Run(async () =>
    {
        await Store.AddToPackAsync(Guid.Parse(destination), selected);
        selected.Clear();
    });

    private Task RemoveSelection() => Run(async () =>
    {
        await Store.RemoveFromPackAsync(activePack!.Value, selected);
        selected.Clear();
    });

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (editing is null) return;
        editDialogHelper ??= await JS.InvokeAsync<IJSObjectReference>("import", "./_content/StickerApp.Shared/dialog.js");
        await editDialogHelper.InvokeVoidAsync("open", editDialog);
    }

    private void EditorKeyDown(Microsoft.AspNetCore.Components.Web.KeyboardEventArgs args)
    {
        if (args.Key == "Escape" && !busy && !customizing) editing = null;
    }

    private async Task Edit(Sticker image)
    {
        if (busy) return;
        editing = image;
        editName = image.Name;
        editSource = editPreview = previews[image.Id];
        // Automatic conversion keeps the GIF original available for trimming and re-editing.
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
        replacement = null;
        transformed = false;
        replacementAnimated = false;
        editError = null;
        gifDuration = trimStart = trimEnd = 0;
        trimmedPreview = null;
        gifThumbnails = [];
        if (IsGif)
        {
            busy = true;
            try
            {
                await InspectGif();
            }
            catch (JSException exception)
            {
                Log.Error("GIF inspection", exception);
                editError = "Could not read this GIF, or it is too large to edit.";
            }
            finally
            {
                busy = false;
            }
        }
    }

    private async Task Replace(InputFileChangeEventArgs e)
    {
        if (busy)
            return;
        busy = true;
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
            busy = false;
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
        gifEditor ??= await JS.InvokeAsync<IJSObjectReference>("import", "./_content/StickerApp.Shared/gif-editor.js");
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

    private void CancelRemoval()
    {
        pendingRemoval = null;
        deletePack = false;
    }

    private Task ConfirmRemoval() => Run(async () =>
    {
        if (deletePack)
        {
            await Store.DeletePackAsync(activePack!.Value);
            activePack = null;
            Navigation.NavigateTo("/");
        }
        else if (pendingRemoval is not null)
            await Store.DeleteStickersAsync([pendingRemoval.Id]);
        CancelRemoval();
    });

    private Task Export() => Run(async () =>
    {
        if (CurrentPack is { } p)
        {
            await Exporter.ExportAsync(p, PackImages);
            notice = Exporter.IsAvailable
                ? "Pack updated. Open WhatsApp’s sticker picker to use it."
                : "Pack export is ready. Open the Android or iPhone app to add it to WhatsApp.";
        }
    });
    public async ValueTask DisposeAsync()
    {
        disposed = true;
        Preparation.Changed -= OnPreparationChanged;
        try
        {
            if (editDialogHelper is not null) await editDialogHelper.DisposeAsync();
            if (gifEditor is not null)
            {
                await gifEditor.InvokeVoidAsync("dispose");
                await gifEditor.DisposeAsync();
            }
        }
        catch (JSDisconnectedException) { }
    }
}
