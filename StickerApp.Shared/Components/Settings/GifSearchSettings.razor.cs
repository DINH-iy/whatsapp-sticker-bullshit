using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace StickerApp.Shared.Components.Settings;

public partial class GifSearchSettings
{
    private ElementReference apiKeyInput;
    private IJSObjectReference? client;
    private bool isReady;
    private bool isConfigured;
    private string? error;
    private string? notice;

    protected override async Task OnAfterRenderInteropAsync(bool firstRender)
    {
        if (!firstRender)
            return;

        try
        {
            client = await JS.InvokeAsync<IJSObjectReference>("import", "./_content/StickerApp.Shared/js/ui/klipy-client.js");
            if (IsDisposed) return;
            isConfigured = await client.InvokeAsync<bool>("hasApiKey");
            if (IsDisposed) return;
            isReady = true;
        }
        catch (JSException)
        {
            error = "GIF search settings could not be loaded. Reload the app to try again.";
        }

        StateHasChanged();
    }

    private async Task SaveKey()
    {
        if (client is null)
            return;
        error = null;
        notice = null;

        try
        {
            // JavaScript reads the input directly so the key never travels to the web host.
            await client.InvokeVoidAsync("saveApiKey", apiKeyInput);
            isConfigured = true;
            notice = "API key saved. Open GIF search to connect to KLIPY.";
        }
        catch (JSException)
        {
            error = "Could not save the key. Enter a key and check that device storage is enabled.";
        }
    }

    private async Task RemoveKey()
    {
        if (client is null)
            return;
        error = null;
        notice = null;

        try
        {
            await client.InvokeVoidAsync("removeApiKey");
            isConfigured = false;
            notice = "API key removed from this device.";
        }
        catch (JSException)
        {
            error = "Could not remove the key. Check that device storage is enabled.";
        }
    }

    protected override async ValueTask DisposeInteropAsync()
    {
        if (client is null)
            return;

        try
        {
            await client.DisposeAsync();
        }
        catch (JSDisconnectedException)
        {
            // The browser may already be closed when Blazor disposes this component.
        }
    }
}
