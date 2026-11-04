using Microsoft.Maui.Storage;
using StickerApp.Shared.Services;

namespace StickerApp.Services;

public sealed class IosBulkImageImporter : IBulkImageImporter
{
    public bool IsAvailable => true;
    public string ActionLabel => "Import images";

    public async Task<IReadOnlyList<BulkImageFile>> PickFolderAsync()
    {
        var files = await FilePicker.Default.PickMultipleAsync(new PickOptions
        {
            PickerTitle = "Choose images to import",
            FileTypes = FilePickerFileType.Images
        });
        if (files is null)
            throw new OperationCanceledException("Image selection was cancelled.");

        var result = new List<BulkImageFile>();
        foreach (var file in files)
        {
            await using var input = await file.OpenReadAsync();
            using var output = new MemoryStream();
            await input.CopyToAsync(output);
            if (output.Length <= Core.LibraryStore.MaxImportBytes)
                result.Add(new BulkImageFile(file.FileName, output.ToArray()));
        }
        return result;
    }
}
