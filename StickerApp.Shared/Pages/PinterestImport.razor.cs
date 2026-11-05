using Microsoft.AspNetCore.Components;
using StickerApp.Core;
using StickerApp.Shared.Services;

namespace StickerApp.Shared.Pages;

public partial class PinterestImport
{
    [SupplyParameterFromQuery(Name = "pack")] public Guid? PackId { get; set; }
    private StickerLibrary library = new();
    private PinterestSelection? selection;
    private PendingPinterestShare? pending;
    private readonly HashSet<string> selected = [];
    private string text = "";
    private string destination = "";
    private string newPackName = "";
    private string destinationLink = "library";
    private string status = "";
    private string? error;
    private string? success;
    private bool busy;
    private bool disposed;
    private CancellationTokenSource? operation;

    private Guid? DestinationId => Guid.TryParse(destination, out var id) ? id : null;
    private bool CreatingPack => destination == "new";
    private string? CapacityError
    {
        get
        {
            if (CreatingPack)
            {
                if (library.Packs.Count >= 10) return "This build supports up to 10 packs. Choose an existing pack or the library.";
                return selected.Count > 30 ? "A pack can contain at most 30 stickers. Select fewer images or choose All images." : null;
            }
            if (DestinationId is not { } id) return null;
            var pack = library.Packs.FirstOrDefault(p => p.Id == id);
            if (pack is null) return "This pack no longer exists. Choose another destination.";
            var remaining = 30 - pack.StickerIds.Count;
            return selected.Count > remaining ? $"This pack has room for {remaining} more stickers. Select fewer images or choose All images." : null;
        }
    }

    protected override async Task OnInitializedAsync()
    {
        library = await Store.ReadAsync();
        Shares.Changed += OnSharedLink;
    }

    protected override void OnParametersSet() => destination = PackId?.ToString() ?? "";

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender) await ReadSharedLink();
    }

    private void OnSharedLink() => _ = InvokeAsync(async () =>
    {
        if (!disposed && !busy && pending is null && selection is null && string.IsNullOrWhiteSpace(text))
            await ReadSharedLink();
    });

    private async Task ReadSharedLink()
    {
        if (busy || disposed) return;
        pending = Inbox.Peek();
        if (pending is null) return;
        text = pending.Url;
        await Load();
    }

    private async Task DismissShare()
    {
        if (pending is not null) Inbox.Remove(pending.Id);
        pending = null;
        text = "";
        selection = null;
        selected.Clear();
        error = null;
        await ReadSharedLink();
    }

    private async Task Load()
    {
        if (busy || disposed) return;
        selection = null;
        selected.Clear();
        await Run(async token =>
        {
            status = "Loading the shared selection from Pinterest…";
            selection = await Importer.LoadAsync(text, token);
            SelectAll();
            library = await Store.ReadAsync();
        });
    }

    private async Task Import()
    {
        if (busy || selection is null || selected.Count == 0 || CapacityError is not null ||
            (CreatingPack && string.IsNullOrWhiteSpace(newPackName))) return;
        var images = selection.Images.Where(image => selected.Contains(image.PinId)).ToList();
        var pack = DestinationId;
        var name = CreatingPack ? newPackName.Trim() : null;
        await Run(async token =>
        {
            status = $"Downloading 0 of {images.Count} images…";
            var progress = new Progress<PinterestImportProgress>(value => _ = InvokeAsync(() =>
            {
                if (disposed || !busy) return;
                status = value.Completed == value.Total ? "Saving images…" : $"Downloading {value.Completed} of {value.Total} images…";
                StateHasChanged();
            }));
            var importedPack = await Importer.ImportAsync(Store, images, pack, progress, token, name);
            success = name is null ? $"Imported {images.Count} images." : $"Created “{name}” with {images.Count} images.";
            destinationLink = importedPack is { } id ? $"packs/{id}" : "library";
            destination = importedPack?.ToString() ?? "";
            newPackName = "";
            selection = null;
            selected.Clear();
            text = "";
            if (pending is not null)
            {
                Inbox.Remove(pending.Id);
                pending = null;
            }
            library = await Store.ReadAsync();
        });
    }

    private async Task Run(Func<CancellationToken, Task> action)
    {
        busy = true;
        error = null;
        success = null;
        using var cancellation = new CancellationTokenSource();
        operation = cancellation;
        try
        {
            var task = action(cancellation.Token);
            if (!disposed) StateHasChanged();
            await task;
        }
        catch (OperationCanceledException)
        {
            error = cancellation.IsCancellationRequested ? "Cancelled. No images were imported." : "The request timed out. Try again.";
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            var reference = Log.Error("Pinterest import", exception);
            error = exception is HttpRequestException { StatusCode: null }
                ? "Could not connect to Pinterest or the importer. Check your connection and try again." : exception.Message;
            error += $" Reference: {reference}.";
        }
        finally
        {
            operation = null;
            busy = false;
            if (!disposed) StateHasChanged();
        }
    }

    private void Toggle(string id) { if (!selected.Add(id)) selected.Remove(id); }
    private void SelectAll() { if (selection is not null) selected.UnionWith(selection.Images.Select(image => image.PinId)); }
    private void ClearSelection() => selected.Clear();
    private void Cancel() => operation?.Cancel();

    public void Dispose()
    {
        disposed = true;
        Shares.Changed -= OnSharedLink;
        Cancel();
    }
}
