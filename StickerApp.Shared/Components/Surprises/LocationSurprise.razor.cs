using Microsoft.JSInterop;

namespace StickerApp.Shared.Components.Surprises;

public partial class LocationSurprise
{
    private IJSObjectReference? module;
    private DotNetObjectReference<LocationSurprise>? reference;
    private bool isVisible;
    private bool isDisposed;

    protected override async Task OnAfterRenderInteropAsync(bool firstRender)
    {
        if (!firstRender) return;

        try
        {
            module = await JS.InvokeAsync<IJSObjectReference>("import", "./_content/StickerApp.Shared/js/ui/location-surprises.js");
            if (IsDisposed) return;
            reference = DotNetObjectReference.Create(this);
            await module.InvokeVoidAsync("start", reference);
            if (IsDisposed) return;
        }
        catch (JSException)
        {
            // Easter eggs are optional; the rest of the app should always remain usable.
        }
    }

    [JSInvokable]
    public Task ShowRomaniaSurprise()
    {
        if (isDisposed) return Task.CompletedTask;
        isVisible = true;
        return InvokeAsync(StateHasChanged);
    }

    [JSInvokable]
    public Task HideSurprise()
    {
        if (isDisposed) return Task.CompletedTask;
        isVisible = false;
        return InvokeAsync(StateHasChanged);
    }

    protected override async ValueTask DisposeInteropAsync()
    {
        isDisposed = true;
        try
        {
            if (module is not null)
            {
                await module.InvokeVoidAsync("stop");
                await module.DisposeAsync();
            }
        }
        catch (JSDisconnectedException)
        {
            // The user may have closed the app before the browser connection shuts down.
        }
        finally
        {
            reference?.Dispose();
        }
    }
}
