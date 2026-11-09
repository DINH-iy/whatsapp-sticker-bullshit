namespace StickerApp.Shared.Services;

/// <summary>Cancels device/network work when the app leaves the foreground.</summary>
public sealed class ForegroundActivity : IDisposable
{
    private CancellationTokenSource foreground = new();
    private bool active = true;
    public void SetActive(bool value)
    {
        if (value == active) return;
        active = value;
        if (!value) foreground.Cancel();
        else { foreground.Dispose(); foreground = new(); }
    }
    public CancellationTokenSource BeginOperation(TimeSpan? timeout = null)
    {
        var operation = CancellationTokenSource.CreateLinkedTokenSource(foreground.Token);
        if (timeout is { } limit) operation.CancelAfter(limit);
        return operation;
    }
    public void Dispose() { foreground.Cancel(); foreground.Dispose(); }
}
