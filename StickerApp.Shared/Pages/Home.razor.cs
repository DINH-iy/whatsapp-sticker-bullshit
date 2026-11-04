using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.JSInterop;
using StickerApp.Core;

namespace StickerApp.Shared.Pages;

public partial class Home
{
    private StickerLibrary library = new();
    private readonly Dictionary<Guid, string> previews = [];
    private readonly HashSet<Guid> selected = [];
    private Guid? activePack;
    // Library filters and pack creation.
    private string search = "";
    private string destination = "";
    private string newPackName = "";
    private string newPackType = "animated";
    private string packName = "";

    // The editor keeps the source separate from its preview so Reset can restore it.
    private string editName = "";
    private string editPreview = "";
    private string editSource = "";
    private bool busy;
    private bool showNewPack;
    private bool deletePack;
    private bool square;
    private bool replacementAnimated;
    private bool transformed;
    private int rotation;
    private string? notice;
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
    private IReadOnlyList<string> Validation => CurrentPack is { } p ? PackRules.Validate(p, PackImages) : [];
    private IEnumerable<Sticker> Visible => (activePack is null
        ? library.Stickers.AsEnumerable().Reverse()
        : PackImages)
        .Where(sticker => sticker.Name.Contains(search, StringComparison.OrdinalIgnoreCase));

    protected override Task OnInitializedAsync() => Run(() => Task.CompletedTask);

    private async Task Reload()
    {
        library = await Store.ReadAsync();
        previews.Clear();
        foreach (var image in library.Stickers)
            previews[image.Id] = await Store.PreviewAsync(image);
        selected.IntersectWith(library.Stickers.Select(s => s.Id));
    }

    private async Task Run(Func<Task> action)
    {
        if (busy)
            return;
        busy = true;
        notice = null;
        try
        {
            await action();
            await Reload();
        }
        catch (Exception e) when (e is IOException or InvalidDataException or InvalidOperationException or NotSupportedException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            notice = e.Message;
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
        List<string> errors = [];
        foreach (var file in e.GetMultipleFiles(30))
        {
            try
            {
                await Store.ImportAsync(file.Name, await ReadFile(file));
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException) { errors.Add($"{file.Name}: {ex.Message}"); }
        }
        if (errors.Count > 0)
            notice = string.Join(" ", errors);
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
            notice = $"Imported “{packName}” as a new pack.";
        }
        finally
        {
            busyText = "Processing…";
        }
    });

    private void SelectPack(ChangeEventArgs e)
    {
        activePack = Guid.TryParse(e.Value?.ToString(), out var id) ? id : null;
        selected.Clear();
        packName = CurrentPack?.Name ?? "";
    }

    private Task CreatePack() => Run(async () =>
    {
        await Store.CreatePackAsync(newPackName, newPackType == "animated");
        showNewPack = false;
        newPackName = "";
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

    private Task RenamePack() => Run(() => Store.RenamePackAsync(activePack!.Value, packName));

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

    private Task Reorder(Guid id, int direction) => Run(() => Store.ReorderAsync(activePack!.Value, id, direction));

    private async Task Edit(Sticker image)
    {
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
        }
        else if (pendingRemoval is not null)
            await Store.DeleteStickersAsync([pendingRemoval.Id]);
        CancelRemoval();
    });

    private Task Export() => Run(async () => { if (CurrentPack is { } p) await Exporter.ExportAsync(p, PackImages); });
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
