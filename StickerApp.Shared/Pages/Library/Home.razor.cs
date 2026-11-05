using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.JSInterop;
using StickerApp.Core;

namespace StickerApp.Shared.Pages.Library;

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
    private bool editorLoading;
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
