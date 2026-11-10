using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.JSInterop;
using StickerApp.Core;

namespace StickerApp.Shared.Pages.Library;

public partial class Home
{
    private bool listView;
    private StickerLibrary library = new();
    private Dictionary<Guid, string> previews = [];
    private readonly HashSet<Guid> selected = [];
    [Parameter] public Guid? PackId { get; set; }
    private Guid? activePack;
    // Library filters and pack creation.
    private string folderFilter = "all", folderName = "", folderDestination = "";
    private Task CreateFolder() => Run(async () => { folderFilter = (await Store.CreateFolderAsync(folderName)).ToString(); folderName = ""; });
    private Task RenameFolder() => Run(async () => { if (Guid.TryParse(folderFilter, out var id)) await Store.RenameFolderAsync(id, folderName); folderName = ""; });
    private Task DeleteFolder() => Run(async () => { if (Guid.TryParse(folderFilter, out var id)) await Store.DeleteFolderAsync(id); folderFilter = "all"; });
    private Task MoveSelectionToFolder() => Run(async () => { await Store.MoveToFolderAsync(selected, Guid.TryParse(folderDestination, out var id) ? id : null); selected.Clear(); });
    private string search = "";
    private string destination = "";
    private LibraryBrowseSnapshot? browse;
    private string typeFilter = "all";
    private string formatFilter = "all";
    private string storageFilter = "all";
    private string editingFilter="all", spoilerFilter="all", favoritesFilter="all", membershipFilter="all";
    private string sort = "default";
    private string? browsePanel;
    private string SmallSizeUnit => Store.DecimalStorageUnits ? "KB" : "KiB";
    private int FilterCount => (folderFilter != "all" ? 1 : 0) + new[] { typeFilter, formatFilter, storageFilter, editingFilter,spoilerFilter,favoritesFilter,membershipFilter }.Count(value => value != "all");

    // The editor keeps the source separate from its preview so Reset can restore it.
    private string editName = "";
    private bool editNsfw;
    private string editPreview = "";
    private string editSource = "";
    private bool busy;
    private bool loadingLibrary;
    private bool editorLoading, editorToolsLoading;
    private bool EditorOpening => editorLoading || editorToolsLoading;
    private bool renamingPack;
    private bool showGifSearch;
    private bool disposed;
    private bool preparationRefreshPending;
    private bool libraryNavigationPending;
    private int previewLoadVersion;
    private Guid? infoImageId;
    private Sticker? InfoImage => library.Stickers.FirstOrDefault(image => image.Id == infoImageId);
    private IEnumerable<Sticker> PreparationImages => CurrentPack is null ? library.Stickers : PackImages;
    private bool IsPreparing(Sticker image) => StickerApp.Shared.Services.StickerPreparationService.NeedsPreparation(image) && Preparation.GetStatus(image) is not { Error: not null } and not { Cancelled: true };
    private int PendingPreparationCount => PreparationImages.Count(IsPreparing);
    private int AttentionCount => PreparationImages.Count(image => Preparation.GetStatus(image)?.Error is not null);
    private bool deletePack;
    private bool deletePackStickers;
    private ElementReference editDialog;
    private IJSObjectReference? editDialogHelper;
    private bool customizing;
    private bool EditingStill => editing is not null && (replacement is not null ? !replacementAnimated : !editing.Animated);
    private bool EditingAnimation => editing is not null && !EditingStill;
    private System.Text.Json.JsonElement? editRecipe;
    private bool replacementAnimated;
    private bool keepEditOriginal;
    private bool transformed;
    private string? notice;
    private bool noticeIsError;
    private string? editError;
    private Sticker? editing;
    private Sticker? pendingRemoval;
    private Sticker? imageToAdd;
    private StickerPack? packToShare;
    private byte[]? replacement;
    private IJSObjectReference? gifEditor, gifSession;
    private string busyText = "Processing…";
    private double gifDuration;
    private double trimStart;
    private double trimEnd;
    private string? trimmedPreview;
    private List<string> gifThumbnails = [];
    private bool IsGif => editSource.StartsWith("data:image/gif;base64,", StringComparison.Ordinal);
    private string GifMax => gifDuration.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
    private bool HasTrim => EditingAnimation && gifDuration > 0 && (trimStart != 0 || trimEnd != gifDuration);
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
    private string? PackHeaderCover => CurrentPack?.CoverImage;
    private IReadOnlyList<PackRules.Issue> Issues => CurrentPack is { } p ? PackRules.Inspect(p, PackImages) : [];
    private IReadOnlyList<string> Validation => CurrentPack is { } p ? PackRules.Validate(p, PackImages) : [];
    private IReadOnlyList<Sticker> Visible => browse is null ? [] : LibraryBrowsing.Images(browse,
        new(search, typeFilter, formatFilter, "all", storageFilter, sort,editingFilter,spoilerFilter,favoritesFilter,membershipFilter), Store.DecimalStorageUnits, activePack).Where(image => folderFilter == "all" || (folderFilter == "none" ? image.FolderId is null : image.FolderId?.ToString() == folderFilter)).ToList();
    private ImageStorageSize ImageSize(Sticker image) => browse?.ImageSizes.GetValueOrDefault(image.Id) ?? new(0, 0);
    private string Size(long bytes) => StorageDisplay.Format(bytes, Store.DecimalStorageUnits);

    private void ResetFilters()
    {
        search = ""; folderFilter = "all";
        typeFilter = formatFilter = storageFilter = editingFilter = spoilerFilter = favoritesFilter = membershipFilter = "all";
    }

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

    protected override Task OnParametersSetAsync() => LoadLibrary();

    private async Task LoadLibrary()
    {
        if (busy) { libraryNavigationPending = true; return; }
        var requestedPack = PackId;
        libraryNavigationPending = false;
        preparationRefreshPending = false;
        loadingLibrary = true;
        try
        {
            await Run(async () =>
            {
                browsePanel = null;
                selected.Clear();
                selectionMode = false;
                pendingBulkRemoval = null;
                renamingPack = false;
                showGifSearch = false;
                infoImageId = null;
                await Reload(requestedPack);
            }, reload: false);
        }
        finally { loadingLibrary = false; }
    }

    private Task Reload() => Reload(PackId);

    private async Task Reload(Guid? requestedPack)
    {
        var version = ++previewLoadVersion;
        using var performance = Log.Measure("Library: read and load previews");
        var snapshot = await Store.ReadBrowseSnapshotAsync();
        var updatedLibrary = snapshot.Library;
        if (disposed || version != previewLoadVersion) return;
        activePack = requestedPack;
        library = updatedLibrary;
        browse = snapshot;
        // Original media is fetched only by near-viewport thumbnail components
        // or explicitly opened editors. No full-library data URL dictionary.
        previews.Clear();
        selected.IntersectWith(library.Stickers.Select(s => s.Id));
    }

    private Task UpdateGroupCollection() => Run(async () =>
    {
        if(CurrentPack is not { } pack)return;
        await new StickerApp.Shared.Services.FriendsPackService(Store,JS).UpdateCollectionAsync(pack.Id);
        notice="Group collection updated.";
    });

    private Task TogglePackVisibility() => Run(async () =>
    {
        if (CurrentPack is not { } pack) return;
        if (pack.Public) await StickerApp.Shared.Services.FriendsPackService.UnpublishAsync(JS, pack.Id);
        await Store.SetPackPublicAsync(pack.Id, !pack.Public);
    });

    private async Task Run(Func<Task> action, [System.Runtime.CompilerServices.CallerMemberName] string operation = "Sticker action", bool reload = true)
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
            if (reload) await Reload();
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
            if (libraryNavigationPending && !disposed)
                await LoadLibrary();
            else if (preparationRefreshPending && !disposed)
            {
                preparationRefreshPending = false;
                await Reload();
            }
        }
    }
    private void BeginRenamePack()
    {
        renamingPack = true;
    }

    private Task PackDetailsSaved(Guid id) => Run(() =>
    {
        renamingPack = false;
        return Task.CompletedTask;
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

    private void CancelRemoval()
    {
        pendingRemoval = null;
        pendingBulkRemoval = null;
        deletePack = false;
        deletePackStickers = false;
    }

    private Task ConfirmPackRemoval(bool deleteStickers)
    {
        deletePackStickers = deleteStickers;
        return ConfirmRemoval();
    }

    private Task ConfirmRemoval() => Run(async () =>
    {
        if (deletePack)
        {
            await StickerApp.Shared.Services.FriendsPackService.UnpublishAsync(JS, activePack!.Value);
            await Preparation.DeletePackAsync(activePack!.Value, deletePackStickers);
            Navigation.NavigateTo("/");
        }
        else if (pendingBulkRemoval is { Count: > 0 } images)
        {
            await Preparation.DeleteStickersAsync(images.Select(image => image.Id));
            selected.ExceptWith(images.Select(image => image.Id));
        }
        else if (pendingRemoval is not null)
            await Preparation.DeleteStickersAsync([pendingRemoval.Id]);
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
    protected override void OnRemoving() => disposed = true;
    protected override async ValueTask DisposeInteropAsync()
    {
        disposed = true;
        preparationLease?.Dispose(); preparationLease = null;
        ++editorSession;
        Preparation.Changed -= OnPreparationChanged;
        try
        {
            if (editDialogHelper is not null) await editDialogHelper.DisposeAsync();
            if (gifSession is not null)
            {
                await gifSession.InvokeVoidAsync("release");
                await gifSession.DisposeAsync();
            }
            if (gifEditor is not null) await gifEditor.DisposeAsync();
        }
        catch (JSDisconnectedException) { }
    }
}
