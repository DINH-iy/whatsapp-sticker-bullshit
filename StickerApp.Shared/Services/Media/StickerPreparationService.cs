using System.Threading.Channels;
using Microsoft.JSInterop;
using StickerApp.Core;

namespace StickerApp.Shared.Services;

/// <summary>One browser/device worker prepares images across page changes, respecting source-retention preferences.</summary>
public sealed class StickerPreparationService(LibraryStore store, IJSRuntime js, AppLog log) : IAsyncDisposable
{
    public sealed record Status(string FileName, bool Working, string? Error = null);
    private readonly Dictionary<Guid, Status> statuses = [];
    private readonly Channel<bool> signals = Channel.CreateBounded<bool>(new BoundedChannelOptions(1)
    { FullMode = BoundedChannelFullMode.DropWrite, SingleReader = true });
    private readonly CancellationTokenSource lifetime = new();
    private IJSObjectReference? worker;
    private Task? runner;
    public event Action<bool>? Changed;

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

    private async Task ProcessAsync()
    {
        var token = lifetime.Token;
        try
        {
            await foreach (var signal in signals.Reader.ReadAllAsync(token))
            {
                var library = await store.ReadAsync();
                foreach (var id in statuses.Keys.Where(id => library.Stickers.All(image => image.Id != id)).ToList())
                    statuses.Remove(id);
                foreach (var image in library.Stickers.Where(NeedsPreparation))
                {
                    token.ThrowIfCancellationRequested();
                    if (GetStatus(image) is not null) continue;
                    log.Info("Automatic sticker preparation", $"Started image {image.Id}; {image.Width}x{image.Height}; {image.MimeType}; {image.Bytes} bytes.");
                    statuses[image.Id] = new(image.FileName, true);
                    Changed?.Invoke(false);
                    try
                    {
                        var source = await store.PreviewAsync(image);
                        if (image.Animated && image.MimeType == "image/webp" && image.OriginalFileName is { } original)
                        {
                            var bytes = await File.ReadAllBytesAsync(store.MediaPath(original), token);
                            if (MediaInspector.Inspect(bytes).MimeType == "image/gif")
                                source = $"data:image/gif;base64,{Convert.ToBase64String(bytes)}";
                        }
                        worker ??= await js.InvokeAsync<IJSObjectReference>("import", token, "./_content/StickerApp.Shared/js/media/conversion/sticker-preparation.js");
                        var result = await worker.InvokeAsync<string>("prepare", token, source, image.Animated);
                        var output = Convert.FromBase64String(result[(result.IndexOf(',') + 1)..]);
                        var info = MediaInspector.Inspect(output);
                        if (info.MimeType != "image/webp" || info.Width != 512 || info.Height != 512 ||
                            info.Animated != image.Animated || output.Length > (image.Animated ? 500 : 100) * 1024 ||
                            (info.Animated && (info.DurationMs is <= 0 or > 10_000 || info.MinimumFrameMs < 8)))
                            throw new InvalidDataException("This image could not be prepared safely. Open Edit to adjust or replace it. Your original is unchanged.");
                        var saved = await store.ReplaceImageAsync(image.Id, output, image.FileName, token);
                        statuses.Remove(image.Id);
                        log.Info("Automatic sticker preparation", $"Image {image.Id}: {(saved ? "saved" : "changed during preparation; output discarded")}; {output.Length} bytes.");
                        // Another window or an edit may have replaced this image while encoding.
                        // Refresh the page from disk in either case.
                        Changed?.Invoke(true);
                        if (!saved) Queue();
                    }
                    catch (JSDisconnectedException) { return; }
                    catch (Exception exception) when (exception is IOException or InvalidOperationException or JSException or UnauthorizedAccessException or FormatException)
                    {
                        var reference = log.Error("Automatic sticker preparation", exception);
                        var message = exception is JSException ? exception.Message.Split('\n')[0] : exception.Message;
                        message = message.Replace("save, then prepare again", "save; preparation will resume automatically")
                            .Replace("then prepare again", "then save; preparation will resume automatically");
                        statuses[image.Id] = new(image.FileName, false, message + $" Reference: {reference}.");
                        Changed?.Invoke(false);
                    }
                }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
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
