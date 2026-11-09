using Microsoft.AspNetCore.Components;
using StickerApp.Core;
using StickerApp.Shared.Services;

namespace StickerApp.Shared.Pages.Pinterest;

public partial class PinterestImport
{
    [SupplyParameterFromQuery(Name = "pack")] public Guid? PackId { get; set; }
    private StickerLibrary library = new();
    private PinterestSelection? selection;
    private PendingPinterestShare? pending;
    private readonly HashSet<string> selected = [];
    private readonly Dictionary<string, bool> keepOriginals = [];
    private string text = "";
    private string destination = "";
    private string newPackName = "";
    private string newPackPublisher = "";
    private string destinationLink = "library";
    private string status = "";
    private string? error;
    private string? success;
    private bool busy;
    private bool disposed;
    private CancellationTokenSource? operation;
    private PinterestImportProgress? importProgress;
    private Stream? videoInput;
    private PinterestImage? videoMedia;
    private TaskCompletionSource<byte[]>? videoResult;
    private int? ProgressPercent => importProgress is { IsSaving: false, Total: > 0 } progress
        ? progress.Completed * 100 / progress.Total : null;
    private string? ProgressDetail => importProgress is { } progress
        ? $"{progress.Completed} of {progress.Total} items ready · {progress.Total - progress.Completed} left"
        : "Finding images in your shared selection…";
    private string? TimeEstimate => importProgress switch
    {
        { IsSaving: true } => "Finishing the import to your library…",
        { IsConvertingVideo: true } => "Choose a clip for the current video. Your whole batch will be saved together.",
        { EstimatedRemaining: { } remaining } => remaining.TotalSeconds < 60
            ? $"About {Math.Max(1, (int)Math.Ceiling(remaining.TotalSeconds))} s left to download"
            : $"About {(int)Math.Ceiling(remaining.TotalMinutes)} min left to download",
        not null => "Estimating download time…",
        _ => null
    };

    private Guid? DestinationId => Guid.TryParse(destination, out var id) ? id : null;
    private bool CreatingPack => destination == "new";
    private string? CapacityError
    {
        get
        {
            if (CreatingPack)
            {
                if (library.Packs.Count >= 10) return "This build supports up to 10 packs. Choose an existing pack or the library.";
                return selected.Count > 30 ? "A pack can contain at most 30 stickers. Select fewer images or choose My Library." : null;
            }
            if (DestinationId is not { } id) return null;
            var pack = library.Packs.FirstOrDefault(p => p.Id == id);
            if (pack is null) return "This pack no longer exists. Choose another destination.";
            var remaining = 30 - pack.StickerIds.Count;
            return selected.Count > remaining ? $"This pack has room for {remaining} more stickers. Select fewer images or choose My Library." : null;
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
            status = "Loading Pinterest images…";
            selection = await Importer.LoadAsync(text, token);
            keepOriginals.Clear();
            foreach (var image in selection.Images) keepOriginals[image.PinId] = image.IsVideo ? Store.KeepOriginalAnimations : Store.KeepOriginalStillImages;
            SelectAll();
            library = await Store.ReadAsync();
        });
    }

    private async Task Import()
    {
        if (busy || selection is null || selected.Count == 0 || CapacityError is not null ||
            (CreatingPack && (string.IsNullOrWhiteSpace(newPackName) || string.IsNullOrWhiteSpace(newPackPublisher)))) return;
        var images = selection.Images.Where(image => selected.Contains(image.PinId)).ToList();
        var pack = DestinationId;
        var name = CreatingPack ? newPackName.Trim() : null;
        var publisher = CreatingPack ? newPackPublisher.Trim() : null;
        await Run(async token =>
        {
            status = "Downloading media…";
            importProgress = new(0, images.Count);
            var progress = new Progress<PinterestImportProgress>(value => _ = InvokeAsync(() =>
            {
                if (disposed || !busy || operation is null || operation.Token != token || token.IsCancellationRequested) return;
                importProgress = value;
                status = value.IsSaving ? "Saving media…" : value.IsConvertingVideo ? "Reviewing video clip…" : "Downloading media…";
                StateHasChanged();
            }));
            var importedPack = await Importer.ImportAsync(Store, images, pack, progress, token, name,
                Store.AskForOriginalOnImport ? keepOriginals : null, publisher, ReviewVideo);
            success = name is null ? $"Imported {images.Count} items." : $"Created “{name}” with {images.Count} items.";
            destinationLink = importedPack is { } id ? $"packs/{id}" : "library";
            destination = importedPack?.ToString() ?? "";
            newPackName = "";
            newPackPublisher = "";
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

    private async Task<byte[]> ReviewVideo(PinterestImage media, string path, CancellationToken token)
    {
        await using var input = File.OpenRead(path);
        var completion = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        await InvokeAsync(() =>
        {
            videoInput = input; videoMedia = media; videoResult = completion;
            if (!disposed) StateHasChanged();
        });
        try { return await completion.Task.WaitAsync(token); }
        finally
        {
            await InvokeAsync(() =>
            {
                if (videoResult == completion) { videoInput = null; videoMedia = null; videoResult = null; }
                if (!disposed) StateHasChanged();
            });
        }
    }
    private void VideoClipReady(byte[] bytes) => videoResult?.TrySetResult(bytes);
    private void CancelVideoClip() { Cancel(); videoResult?.TrySetCanceled(); }

    private async Task Run(Func<CancellationToken, Task> action)
    {
        busy = true;
        importProgress = null;
        error = null;
        success = null;
        using var cancellation = Activity.BeginOperation();
        operation = cancellation;
        try
        {
            var task = action(cancellation.Token);
            if (!disposed) StateHasChanged();
            await task;
        }
        catch (OperationCanceledException)
        {
            error = cancellation.IsCancellationRequested ? "Cancelled. No media was imported." : "The request timed out. Try again.";
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
    private void Cancel()
    {
        operation?.Cancel();
        status = "Cancelling…";
    }

    public void Dispose()
    {
        disposed = true;
        Shares.Changed -= OnSharedLink;
        Cancel();
    }
}
