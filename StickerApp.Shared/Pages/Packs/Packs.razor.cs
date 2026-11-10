using Microsoft.AspNetCore.Components.Forms;
using StickerApp.Core;

namespace StickerApp.Shared.Pages.Packs;

public partial class Packs
{
    private bool listView;
    private StickerLibrary library = new();
    private readonly Dictionary<Guid, string> covers = [];
    private string? error;
    private bool busy;
    private bool showCreate;
    private string ownershipFilter="all";
    private StickerPack? editingPack, deletingPack;
    private LibraryBrowseSnapshot? browse;
    private string search = "";
    private string visibilityFilter="all",spoilerFilter="all",contentFilter="all";
    private string sort = "default";
    private string? browsePanel;
    private string typeFilter = "all";
    private string countFilter = "all";
    private string storageFilter = "all";
    private string SizeUnit => Store.DecimalStorageUnits ? "MB" : "MiB";
    private int FilterCount => new[] { typeFilter, countFilter, storageFilter, ownershipFilter,visibilityFilter,spoilerFilter,contentFilter }.Count(value => value != "all");
    private IReadOnlyList<StickerPack> VisiblePacks => browse is null ? [] : LibraryBrowsing.Packs(browse,
        new(search, typeFilter, countFilter, storageFilter, sort, ownershipFilter,visibilityFilter,spoilerFilter,contentFilter), Store.DecimalStorageUnits);
    private string PackSize(StickerPack pack) => StorageDisplay.Format(
        browse?.PackSizes.TryGetValue(pack.Id, out var size) == true ? size.StickerBytes + size.SourceBytes : 0,
        Store.DecimalStorageUnits);
    private string PackTypeLabel(StickerPack pack) => LibraryBrowsing.PackType(pack, library.Stickers) switch
        { "still" => "Still", "animated" => "Animated", "mixed" => "Mixed", _ => "Empty" };

    private void ResetFilters()
    {
        search = "";
        typeFilter = countFilter = storageFilter = ownershipFilter = visibilityFilter = spoilerFilter = contentFilter = "all";
    }

    private Task DeletePack(bool deleteStickers)=>Run(async()=>{
        if(deletingPack is not { } pack)return;
        await StickerApp.Shared.Services.FriendsPackService.UnpublishAsync(JS,pack.Id);
        await Preparation.DeletePackAsync(pack.Id, deleteStickers);
        deletingPack=null;
        await Reload();
    });

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
        catch (Exception exception) when (exception is IOException or InvalidDataException or InvalidOperationException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            var reference = Log.Error("Pack action", exception);
            error = exception.Message + $" Reference: {reference}.";
        }
        finally { busy = false; }
    }
}
