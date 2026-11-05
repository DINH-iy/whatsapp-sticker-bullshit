using Microsoft.AspNetCore.Components.Forms;
using StickerApp.Core;

namespace StickerApp.Shared.Pages.Packs;

public partial class Packs
{
    private StickerLibrary library = new();
    private readonly Dictionary<Guid, string> covers = [];
    private string? error;
    private bool busy;
    private bool showCreate;
    private StickerPack? editingPack;
    private LibraryBrowseSnapshot? browse;
    private string search = "";
    private string sort = "default";
    private string? browsePanel;
    private string typeFilter = "all";
    private string countFilter = "all";
    private string storageFilter = "all";
    private string SizeUnit => Store.DecimalStorageUnits ? "MB" : "MiB";
    private int FilterCount => new[] { typeFilter, countFilter, storageFilter }.Count(value => value != "all");
    private IReadOnlyList<StickerPack> VisiblePacks => browse is null ? [] : LibraryBrowsing.Packs(browse,
        new(search, typeFilter, countFilter, storageFilter, sort), Store.DecimalStorageUnits);
    private string PackSize(StickerPack pack) => StorageDisplay.Format(
        browse?.PackSizes.TryGetValue(pack.Id, out var size) == true ? size.StickerBytes + size.SourceBytes : 0,
        Store.DecimalStorageUnits);
    private string PackTypeLabel(StickerPack pack) => LibraryBrowsing.PackType(pack, library.Stickers) switch
        { "still" => "Still", "animated" => "Animated", "mixed" => "Mixed", _ => "Empty" };

    private void ResetFilters()
    {
        search = "";
        typeFilter = countFilter = storageFilter = "all";
    }

    private Task PackDetailsSaved(Guid id) => Run(async () =>
    {
        editingPack = null;
        await Reload();
    });

    protected override Task OnInitializedAsync() => Run(Reload);

    private async Task Reload()
    {
        browse = await Store.ReadBrowseSnapshotAsync();
        library = browse.Library;
        covers.Clear();
        foreach (var pack in library.Packs)
        {
            if (pack.CoverImage is not null)
                covers[pack.Id] = pack.CoverImage;
            else if (library.Stickers.FirstOrDefault(image => image.Id == pack.StickerIds.FirstOrDefault()) is { } first)
                covers[pack.Id] = await Store.PreviewAsync(first);
        }
    }

    private void PackCreated(Guid id)
    {
        showCreate = false;
        Navigation.NavigateTo($"packs/{id}");
    }

    private Task ImportPack(InputFileChangeEventArgs args) => Run(async () =>
    {
        await using var input = args.File.OpenReadStream(PackArchive.MaxArchiveBytes);
        var id = await Store.ImportPackAsync(input);
        Navigation.NavigateTo($"packs/{id}");
    });

    private async Task Run(Func<Task> action)
    {
        if (busy) return;
        busy = true;
        error = null;
        try { await action(); }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            var reference = Log.Error("Pack action", exception);
            error = exception.Message + $" Reference: {reference}.";
        }
        finally { busy = false; }
    }
}
