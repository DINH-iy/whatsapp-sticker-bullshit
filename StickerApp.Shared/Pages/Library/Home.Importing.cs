using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.JSInterop;
using StickerApp.Core;

namespace StickerApp.Shared.Pages.Library;

public partial class Home
{
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
}
