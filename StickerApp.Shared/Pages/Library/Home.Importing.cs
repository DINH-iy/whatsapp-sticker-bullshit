using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.JSInterop;
using StickerApp.Core;
using StickerApp.Shared.Components.Library;

namespace StickerApp.Shared.Pages.Library;

public partial class Home
{
    private sealed record PendingImageImport(OriginalImportChoice Choice, Func<Task<byte[]>> ReadBytes);
    private List<PendingImageImport>? pendingImports;
    private Guid? pendingImportPackId;
    private string? pendingImportNote;
    private int importPickerVersion;
    private bool showVideoImport;
    private async Task VideoImported(Guid id)
    {
        showVideoImport = false;
        await Reload();
        notice = "GIF added. It will be ready to edit when sticker preparation finishes.";
        noticeIsError = false;
    }

    private static async Task<byte[]> ReadFile(IBrowserFile file)
    {
        var gif = file.ContentType == "image/gif" || Path.GetExtension(file.Name).Equals(".gif", StringComparison.OrdinalIgnoreCase);
        if (file.Size > (gif ? LibraryStore.MaxGifImportBytes : LibraryStore.MaxImportBytes))
            throw new InvalidDataException(gif ? LibraryStore.GifSizeError : "Choose a file smaller than 10 MB.");
        await using var stream = file.OpenReadStream(LibraryStore.MaxGifImportBytes);
        using var memory = new MemoryStream();
        await stream.CopyToAsync(memory);
        return memory.ToArray();
    }

    private Task Import(InputFileChangeEventArgs e) => Run(async () =>
    {
        var files = e.GetMultipleFiles(30).Select(file =>
        {
            var animated = file.ContentType == "image/gif" || Path.GetExtension(file.Name).Equals(".gif", StringComparison.OrdinalIgnoreCase);
            return new PendingImageImport(new(file.Name, file.Size, animated,
                animated ? Store.KeepOriginalAnimations : Store.KeepOriginalStillImages), () => ReadFile(file));
        }).ToList();
        await BeginImageImport(files, activePack);
    });

    private async Task BeginImageImport(List<PendingImageImport> files, Guid? packId, string? note = null)
    {
        if (files.Count == 0) return;
        if (Store.AskForOriginalOnImport)
        {
            pendingImports = files;
            pendingImportPackId = packId;
            pendingImportNote = note;
            return;
        }
        await ImportImages(files, packId, true, note);
    }

    private Task ConfirmImageImport(bool useDefaults) => Run(async () =>
    {
        if (pendingImports is null) return;
        if (useDefaults) await Store.SetAskForOriginalOnImportAsync(false);
        var files = pendingImports;
        var packId = pendingImportPackId;
        var note = pendingImportNote;
        pendingImports = null;
        await ImportImages(files, packId, useDefaults, note);
    });

    private void CancelImageImport()
    {
        pendingImports = null;
        pendingImportNote = null;
        pendingImportPackId = null;
        importPickerVersion++;
    }

    private async Task ImportImages(IReadOnlyList<PendingImageImport> files, Guid? packId, bool useDefaults, string? note)
    {
        var imported = 0;
        List<string> errors = [];
        foreach (var file in files)
        {
            try
            {
                await Store.ImportAsync(file.Choice.Name, await file.ReadBytes(), packId,
                    useDefaults ? null : file.Choice.KeepOriginal);
                imported++;
            }
            catch (Exception exception) when (exception is IOException or InvalidDataException or InvalidOperationException or UnauthorizedAccessException)
            {
                Log.Error("Image import", exception);
                errors.Add($"{file.Choice.Name}: {exception.Message}");
            }
        }
        notice = $"Added {imported} of {files.Count} files.";
        importPickerVersion++;
        if (note is not null) notice += " " + note;
        if (errors.Count > 0)
        {
            notice += " " + string.Join(" ", errors);
            noticeIsError = true;
        }
    }

    private Task ImportFolder() => Run(async () =>
    {
        var files = await BulkImporter.PickFolderAsync();
        var pending = files.Take(30).Select(file =>
        {
            var info = MediaInspector.Inspect(file.Bytes);
            return new PendingImageImport(new(file.Name, file.Bytes.Length, info.Animated,
                info.Animated ? Store.KeepOriginalAnimations : Store.KeepOriginalStillImages), () => Task.FromResult(file.Bytes));
        }).ToList();
        await BeginImageImport(pending, activePack, files.Count > 30 ? $"Only the first 30 supported images were selected; {files.Count - 30} were skipped." : null);
    });

    private Task ImportPack(InputFileChangeEventArgs args) => Run(async () =>
    {
        busyText = "Importing pack…";
        try
        {
            await using var input = args.File.OpenReadStream(PackArchive.MaxArchiveBytes);
            activePack = await Store.ImportPackAsync(input);
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
}
