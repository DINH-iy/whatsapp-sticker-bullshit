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
    private bool deletePack;
    private bool square;
    private bool replacementAnimated;
    private bool transformed;
    private int rotation;
    private string? notice;
    private bool noticeIsError;
    private string? editError;
    private Sticker? editing;
    private Sticker? pendingRemoval;
    private Sticker? imageToAdd;
    private StickerPack? packToShare;
    private byte[]? replacement;
    private IJSObjectReference? editor;
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
    private IReadOnlyList<PackRules.Issue> Issues => CurrentPack is { } p ? PackRules.Inspect(p, PackImages) : [];
    private IReadOnlyList<string> Validation => CurrentPack is { } p ? PackRules.Validate(p, PackImages) : [];
    private IEnumerable<Sticker> Visible => (activePack is null
        ? library.Stickers.AsEnumerable().Reverse()
        : PackImages)
        .Where(sticker => sticker.Name.Contains(search, StringComparison.OrdinalIgnoreCase));

    protected override Task OnParametersSetAsync() => Run(() =>
    {
        activePack = PackId;
        selected.Clear();
        renamingPack = false;
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

    private async Task Run(Func<Task> action)
    {
        if (busy)
            return;
        busy = true;
        notice = null;
        noticeIsError = false;
        try
        {
            await action();
            await Reload();
        }
        catch (Exception e) when (e is IOException or InvalidDataException or InvalidOperationException or NotSupportedException or JSException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            notice = e is JSException ? e.Message.Split('\n')[0] : e.Message;
            noticeIsError = true;
        }
        finally
        {
            busy = false;
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
            catch (Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException) { errors.Add($"{file.Name}: {ex.Message}"); }
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

    private async Task Edit(Sticker image)
    {
        if (busy) return;
        editing = image;
        editName = image.Name;
        editSource = editPreview = previews[image.Id];
        replacement = null;
        rotation = 0;
        square = false;
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
            catch (JSException)
            {
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
            rotation = 0;
            square = false;
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
            editError = ex is JSException ? "Could not read this GIF, or it is too large to edit." : ex.Message;
        }
        finally
        {
            busy = false;
        }
    }

    private Task Rotate()
    {
        rotation = (rotation + 90) % 360;
        return Transform();
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
            editError = ex is JSException ? "Could not trim this GIF. Try a shorter selection or a smaller GIF." : ex.Message;
        }
        finally
        {
            busy = false;
            busyText = "Processing…";
        }
    }

    private Task Crop()
    {
        square = true;
        return Transform();
    }

    private void ResetImage()
    {
        editPreview = editSource;
        rotation = 0;
        square = false;
        transformed = false;
        editError = null;
    }

    private async Task Transform()
    {
        if (busy)
            return;
        busy = true;
        editError = null;
        try
        {
            editor ??= await JS.InvokeAsync<IJSObjectReference>("import", "./_content/StickerApp.Shared/image-editor.js");
            editPreview = await editor.InvokeAsync<string>("transform", editSource, rotation, square);
            transformed = true;
        }
        catch (JSException)
        {
            editError = "Could not edit this image.";
        }
        finally
        {
            busy = false;
        }
    }

    private async Task Save()
    {
        if (busy || editing is null)
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

    private async Task FixIssue(PackRules.Issue issue)
    {
        var image = library.Stickers.FirstOrDefault(image => image.Id == issue.StickerId);
        if (image is null) return;
        if (issue.Action != "Prepare sticker") { await Edit(image); return; }
        await Run(async () =>
        {
            busyText = "Preparing sticker…";
            try
            {
                await using var converter = await JS.InvokeAsync<IJSObjectReference>("import", "./_content/StickerApp.Shared/sticker-converter.js");
                var result = await converter.InvokeAsync<string>("prepare", previews[image.Id], image.Animated);
                var bytes = Convert.FromBase64String(result[(result.IndexOf(',') + 1)..]);
                var info = MediaInspector.Inspect(bytes);
                if (info.MimeType != "image/webp" || info.Width != 512 || info.Height != 512 ||
                    info.Animated != image.Animated || bytes.Length > (image.Animated ? 500 : 100) * 1024)
                    throw new InvalidOperationException("The prepared image did not meet the sticker requirements. Your image was kept unchanged.");
                await Store.ReplaceImageAsync(image.Id, bytes);
                notice = $"Prepared “{image.Name}”: 512 × 512 WebP, {Math.Ceiling(bytes.Length / 1024d):0} KB. Original file kept.";
            }
            finally { busyText = "Processing…"; }
        });
    }

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
        try
        {
            if (editor is not null)
                await editor.DisposeAsync();
            if (gifEditor is not null)
            {
                await gifEditor.InvokeVoidAsync("dispose");
                await gifEditor.DisposeAsync();
            }
        }
        catch (JSDisconnectedException) { }
    }
}
