using System.Threading.Channels;
using Microsoft.JSInterop;
using StickerApp.Core;

namespace StickerApp.Shared.Services;

/// <summary>One browser/device worker prepares images across page changes, respecting source-retention preferences.</summary>
public sealed class StickerPreparationService(LibraryStore store, IJSRuntime js, AppLog log) : IAsyncDisposable
{
    public sealed record Status(string FileName, bool Working, string? Error = null, bool Cancelled = false, Progress? Progress = null);
    public sealed record Progress(int Percent, int Completed, int Total, int Attempt, int Attempts);
    private readonly Dictionary<Guid, Status> statuses = [];
    private readonly Channel<bool> signals = Channel.CreateBounded<bool>(new BoundedChannelOptions(1)
    { FullMode = BoundedChannelFullMode.DropWrite, SingleReader = true });
    private readonly CancellationTokenSource lifetime = new();
    private IJSObjectReference? worker;
    private Task? runner;
    private bool foreground;
    private int editorLeases;
    private CancellationTokenSource? jobCancellation;
    private TaskCompletionSource? jobFinished;
    private Guid? activeId;
    [JSInvokable] public Task PreparationProgress(Progress progress)
    {
        if (activeId is { } id && jobCancellation?.IsCancellationRequested == false && statuses.TryGetValue(id, out var status) && status.Working)
        {
            statuses[id] = status with { Progress = progress };
            Changed?.Invoke(false);
        }
        return Task.CompletedTask;
    }
    public async Task SetForegroundAsync(bool value)
    {
        foreground = value;
        if (!value) await StopJobAsync();
        else Queue();
    }
    private async Task StopJobAsync()
    {
        var finished = jobFinished?.Task;
        jobCancellation?.Cancel();
        if (worker is not null) await worker.InvokeVoidAsync("cancel");
        if (finished is not null) await finished;
    }
    public async Task<IDisposable> SuspendForEditingAsync()
    {
        ++editorLeases;
        try { await StopJobAsync(); return new EditorLease(this); }
        catch { --editorLeases; Queue(); throw; }
    }
    private sealed class EditorLease(StickerPreparationService owner) : IDisposable
    {
        private StickerPreparationService? service = owner;
        public void Dispose() { if (service is not { } current) return; service = null; --current.editorLeases; current.Queue(); }
    }
    public event Action<bool>? Changed;
    public string? ActiveName { get; private set; }

    public static bool NeedsPreparation(Sticker image) => image.MimeType != "image/webp" || image.Width != 512 ||
        image.Height != 512 || image.Bytes <= 0 || image.Bytes > (image.Animated ? 500 : 100) * 1024 ||
        (image.Animated && (image.DurationMs is <= 0 or > 10_000 || image.MinimumFrameMs < 8));

    public Status? GetStatus(Sticker image) => statuses.TryGetValue(image.Id, out var state) && state.FileName == image.FileName ? state : null;

    public void Start()
    {
        if (runner is not null) return;
        store.Changed += Queue;
        Queue();
        runner = ProcessAsync();
    }

    private void Queue() => signals.Writer.TryWrite(true);

    public void Retry(Guid id)
    {
        statuses.Remove(id);
        Queue();
        Changed?.Invoke(false);
    }

    public Task CancelAsync(Guid id) => CancelManyAsync([id]);

    private async Task CancelManyAsync(IEnumerable<Guid> ids)
    {
        // Pause selection while the worker is being stopped, so cancellation
        // cannot accidentally target the next sticker in the queue.
        ++editorLeases;
        try
        {
            var selected = ids.ToHashSet();
            var images = (await store.ReadAsync()).Stickers.Where(image => selected.Contains(image.Id) && NeedsPreparation(image));
            foreach (var image in images)
            {
                statuses[image.Id] = new(image.FileName, false, Cancelled: true);
                if (activeId == image.Id)
                {
                    jobCancellation?.Cancel();
                    if (worker is not null)
                    {
                        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                        try { await worker.InvokeVoidAsync("cancel", timeout.Token); }
                        catch (OperationCanceledException) when (timeout.IsCancellationRequested) { }
                        catch (JSDisconnectedException) { }
                        catch (JSException exception) { log.Error("Stop sticker preparation", exception); }
                    }
                }
            }
            Changed?.Invoke(false);
        }
        finally { --editorLeases; Queue(); }
    }

    public async Task DeleteStickersAsync(IEnumerable<Guid> ids)
    {
        var remove = ids.Distinct().ToArray();
        ++editorLeases;
        try
        {
            await CancelManyAsync(remove);
            await store.DeleteStickersAsync(remove);
        }
        finally { --editorLeases; Queue(); }
    }

    public async Task DeletePackAsync(Guid id, bool deleteStickers)
    {
        ++editorLeases;
        try
        {
            if (deleteStickers)
            {
                var pack = (await store.ReadAsync()).Packs.FirstOrDefault(pack => pack.Id == id);
                if (pack is not null)
                    await CancelManyAsync(pack.StickerIds);
            }
            await store.DeletePackAsync(id, deleteStickers);
        }
        finally { --editorLeases; Queue(); }
    }

    private async Task ProcessAsync()
    {
        var token = lifetime.Token;
        try
        {
            await foreach (var signal in signals.Reader.ReadAllAsync(token))
            {
                if (!foreground || editorLeases > 0) continue;
                var library = await store.ReadAsync();
                foreach (var id in statuses.Keys.Where(id => library.Stickers.All(image => image.Id != id)).ToList())
                    statuses.Remove(id);
                foreach (var image in library.Stickers.Where(NeedsPreparation))
                {
                    token.ThrowIfCancellationRequested();
                    if (!foreground || editorLeases > 0) break;
                    if (GetStatus(image) is not null) continue;
                    // A queued snapshot can outlive a deletion or replacement.
                    if (!(await store.ReadAsync()).Stickers.Any(current => current.Id == image.Id && current.FileName == image.FileName)) continue;
                    if (!foreground || editorLeases > 0 || GetStatus(image) is not null) continue;
                    log.Info("Automatic sticker preparation", $"Started image {image.Id}; {image.Width}x{image.Height}; {image.MimeType}; {image.Bytes} bytes.");
                    ActiveName = image.Name;
                    statuses[image.Id] = new(image.FileName, true);
                    using var job = CancellationTokenSource.CreateLinkedTokenSource(token);
                    jobCancellation = job;
                    activeId = image.Id;
                    jobFinished = new(TaskCreationOptions.RunContinuationsAsynchronously);
                    Changed?.Invoke(false);
                    var jobToken = job.Token;
                    using var performance = log.Measure("Sticker preparation: total", $"animated={image.Animated}; bytes={image.Bytes}; pixels={image.Width}x{image.Height}");
                    try
                    {
                        var source = await store.PreviewAsync(image);
                        if (image.Animated && image.MimeType == "image/webp" && image.OriginalFileName is { } original)
                        {
                            var bytes = await File.ReadAllBytesAsync(store.MediaPath(original), jobToken);
                            if (MediaInspector.Inspect(bytes).MimeType == "image/gif")
                                source = $"data:image/gif;base64,{Convert.ToBase64String(bytes)}";
                        }
                        worker ??= await js.InvokeAsync<IJSObjectReference>("import", jobToken, "./_content/StickerApp.Shared/js/media/conversion/sticker-preparation.js");
                        using var progressReference = DotNetObjectReference.Create(this);
                        var result = await worker.InvokeAsync<string>("prepare", jobToken, source, image.Animated, progressReference);
                        jobToken.ThrowIfCancellationRequested();
                        var output = Convert.FromBase64String(result[(result.IndexOf(',') + 1)..]);
                        var info = MediaInspector.Inspect(output);
                        if (info.MimeType != "image/webp" || info.Width != 512 || info.Height != 512 ||
                            info.Animated != image.Animated || output.Length > (image.Animated ? 500 : 100) * 1024 ||
                            (info.Animated && (info.DurationMs is <= 0 or > 10_000 || info.MinimumFrameMs < 8)))
                            throw new InvalidDataException("This image could not be prepared safely. Open Edit to adjust or replace it. Your original is unchanged.");
                        var saved = await store.ReplaceImageAsync(image.Id, output, image.FileName, jobToken);
                        statuses.Remove(image.Id);
                        log.Info("Automatic sticker preparation", $"Image {image.Id}: {(saved ? "saved" : "changed during preparation; output discarded")}; {output.Length} bytes.");
                        // Another window or an edit may have replaced this image while encoding.
                        // Refresh the page from disk in either case.
                        Changed?.Invoke(true);
                        if (!saved) Queue();
                    }
                    catch (OperationCanceledException) when (jobToken.IsCancellationRequested) { ClearInterruptedStatus(image); }
                    catch (JSException) when (jobToken.IsCancellationRequested) { ClearInterruptedStatus(image); }
                    catch (JSDisconnectedException) { return; }
                    catch (Exception exception) when (exception is IOException or InvalidDataException or InvalidOperationException or JSException or UnauthorizedAccessException or FormatException)
                    {
                        var reference = log.Error("Automatic sticker preparation", exception);
                        var message = exception is JSException ? exception.Message.Split('\n')[0] : exception.Message;
                        message = message.Replace("save, then prepare again", "save; preparation will resume automatically")
                            .Replace("then prepare again", "then save; preparation will resume automatically");
                        statuses[image.Id] = new(image.FileName, false, message + $" Reference: {reference}.");
                        Changed?.Invoke(false);
                    }
                    finally {
                        jobCancellation = null;
                        activeId = null;
                        jobFinished.TrySetResult(); jobFinished = null;
                        ActiveName = null; Changed?.Invoke(false);
                    }
                }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }

    private void ClearInterruptedStatus(Sticker image)
    {
        if (GetStatus(image)?.Cancelled != true) statuses.Remove(image.Id);
    }

    public async ValueTask DisposeAsync()
    {
        store.Changed -= Queue;
        lifetime.Cancel();
        signals.Writer.TryComplete();
        try
        {
            if (worker is not null) await worker.InvokeVoidAsync("cancel");
            if (runner is not null) await runner;
            if (worker is not null) await worker.DisposeAsync();
        }
        catch (JSDisconnectedException) { }
        finally { lifetime.Dispose(); }
    }
}
