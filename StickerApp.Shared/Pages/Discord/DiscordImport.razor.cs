using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using StickerApp.Core;

namespace StickerApp.Shared.Pages.Discord;

public partial class DiscordImport
{
    [SupplyParameterFromQuery(Name = "pack")] public Guid? PackId { get; set; }
    private readonly DiscordImportClient client = new();
    private ElementReference fileInput;
    private IJSObjectReference? module, watcher;
    private DotNetObjectReference<DiscordImport>? reference;
    private List<DiscordFavorite> items = [];
    private List<StickerPack> packs = [];
    private readonly HashSet<string> selected = [], imported = [], previews = [];
    private string search = "", destination = "", newPackName = "Discord favourites", publisher = "";
    private string? error, notice, videoName;
    private bool ready, busy, reading, disposed, keepOriginals;
    private int page, skipped, duplicates;
    private IEnumerable<DiscordFavorite> Filtered => items.Where(item => item.Name.Contains(search, StringComparison.OrdinalIgnoreCase));
    private int Pages => Math.Max(1, (Filtered.Count() + 11) / 12);
    private IEnumerable<DiscordFavorite> Visible => Filtered.Skip(page * 12).Take(12);
    private DiscordImportProgress progress = new(0, 0);
    private CancellationTokenSource? operation;
    private MemoryStream? videoStream;
    private TaskCompletionSource<byte[]>? videoCompletion;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender) return;
        packs = (await Store.ReadAsync()).Packs;
        destination = PackId?.ToString() ?? "";
        publisher = (await Store.ReadProfileAsync()).Username;
        keepOriginals = Store.KeepOriginalAnimations;
        module = await JS.InvokeAsync<IJSObjectReference>("import", "./_content/StickerApp.Shared/js/imports/discord/discord-export.js");
        reference = DotNetObjectReference.Create(this);
        watcher = await module.InvokeAsync<IJSObjectReference>("watch", fileInput, reference);
        ready = true; StateHasChanged();
    }
    [JSInvokable] public Task DiscordExportReading() => InvokeAsync(() => { reading = true; error = notice = null; StateHasChanged(); });
    [JSInvokable] public Task DiscordExportFailed(string message) => InvokeAsync(() => { reading = false; error = message; StateHasChanged(); });
    [JSInvokable] public Task DiscordExportLoaded(DiscordExportSelection result) => InvokeAsync(() =>
    {
        reading = false; items = result.Items; skipped = result.Skipped; duplicates = result.DuplicateCount;
        selected.Clear(); previews.Clear(); imported.Clear(); page = 0; search = "";
        if (items.Count == 0) notice = "This export contains no supported GIF favourites.";
        StateHasChanged();
    });
    private async Task CancelReading()
    {
        if (watcher is not null) await watcher.InvokeVoidAsync("cancel");
        reading = false;
    }
    private void ResetPage() => page = 0;
    private void Toggle(DiscordFavorite item) { if (!selected.Remove(item.Id) && selected.Count < 30) selected.Add(item.Id); }
    private void SelectPage()
    {
        foreach (var item in Visible.Where(item => item.Problem is null && !imported.Contains(item.Id)))
            if (selected.Count < 30) selected.Add(item.Id);
    }
    private async Task Import()
    {
        if (busy) return;
        var batch = items.Where(item => selected.Contains(item.Id)).ToArray();
        busy = true; error = notice = null; operation = new(); progress = new(0, batch.Length);
        try
        {
            var reporter = new Progress<DiscordImportProgress>(value => { if (!disposed) { progress = value; _ = InvokeAsync(StateHasChanged); } });
            await client.ImportAsync(Store, batch, Guid.TryParse(destination, out var id) ? id : null,
                ReviewVideo, reporter, operation.Token, destination == "new" ? newPackName : null,
                destination == "new" ? publisher : null, Store.AskForOriginalOnImport ? batch.ToDictionary(item => item.Id, _ => keepOriginals) : null);
            foreach (var item in batch) imported.Add(item.Id);
            selected.Clear(); previews.Clear(); packs = (await Store.ReadAsync()).Packs;
            notice = $"Imported {batch.Length} favourites.";
            if (destination == "new") destination = "";
        }
        catch (OperationCanceledException) { notice = operation.IsCancellationRequested ? "Import cancelled. No stickers were saved." : "The media request timed out. No stickers were saved; try a smaller batch."; }
        catch (Exception ex) when (ex is IOException or HttpRequestException or InvalidOperationException or UnauthorizedAccessException) { error = ex.Message; }
        finally { busy = false; operation.Dispose(); operation = null; }
    }
    private async Task<byte[]> ReviewVideo(DiscordFavorite item, byte[] bytes, CancellationToken token)
    {
        videoName = item.Name; videoStream = new(bytes, false);
        videoCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously); StateHasChanged();
        try { return await videoCompletion.Task.WaitAsync(token); }
        finally { videoStream.Dispose(); videoStream = null; videoCompletion = null; }
    }
    private void ClipReady(byte[] bytes) => videoCompletion?.TrySetResult(bytes);
    private void Cancel() { if (!progress.Saving) { operation?.Cancel(); videoCompletion?.TrySetCanceled(); } }
    public async ValueTask DisposeAsync()
    {
        disposed = true; operation?.Cancel(); videoCompletion?.TrySetCanceled();
        try { if (watcher is not null) { await watcher.InvokeVoidAsync("dispose"); await watcher.DisposeAsync(); } if (module is not null) await module.DisposeAsync(); }
        catch (JSDisconnectedException) { }
        reference?.Dispose(); client.Dispose();
    }
}
