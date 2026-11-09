using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace StickerApp.Shared.Components.Infrastructure;

/// <summary>Owns interop initialization until it completes, even if rendering removes the component.</summary>
public abstract class InteropComponentBase : ComponentBase, IAsyncDisposable
{
    private readonly SemaphoreSlim renderGate = new(1, 1);
    private readonly object cleanupGate = new();
    private Task? cleanup;
    private int disposed, activeOperations;
    protected bool IsDisposed => Volatile.Read(ref disposed) != 0;

    protected sealed override async Task OnAfterRenderAsync(bool firstRender)
    {
        await renderGate.WaitAsync();
        try { await RunInteropAsync(() => OnAfterRenderInteropAsync(firstRender)); }
        finally { renderGate.Release(); }
    }

    protected async Task RunInteropAsync(Func<Task> action)
    {
        Interlocked.Increment(ref activeOperations);
        try
        {
            if (!IsDisposed) await action();
        }
        catch (JSDisconnectedException) { }
        catch (JSException) when (IsDisposed) { }
        catch (ObjectDisposedException) when (IsDisposed) { }
        catch (OperationCanceledException) when (IsDisposed) { }
        finally
        {
            if (Interlocked.Decrement(ref activeOperations) == 0 && IsDisposed) await CleanupOnceAsync();
        }
    }

    protected virtual Task OnAfterRenderInteropAsync(bool firstRender) => Task.CompletedTask;
    protected virtual ValueTask DisposeInteropAsync() => ValueTask.CompletedTask;
    protected virtual void OnRemoving() { }
    protected virtual ValueTask StopInteropAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) == 0)
        {
            OnRemoving();
            try { await StopInteropAsync(); }
            catch (JSDisconnectedException) { }
            catch (JSException) when (IsDisposed) { }
        }
        // Do not block renderer removal on an unfinished JS call. Its continuation
        // owns cleanup; this also releases references returned after removal.
        if (Volatile.Read(ref activeOperations) == 0) await CleanupOnceAsync();
    }

    private Task CleanupOnceAsync()
    {
        lock (cleanupGate) return cleanup ??= CleanupAsync();
    }

    private async Task CleanupAsync()
    {
        try { await DisposeInteropAsync(); }
        catch (JSDisconnectedException) { }
    }
}
