using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace StickerApp.Shared.Components;

public partial class SurpriseSettings
{
    private IJSObjectReference? module;
    private bool enabled;
    private bool isReady;
    private string? error;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender) return;

        try
        {
            module = await JS.InvokeAsync<IJSObjectReference>("import", "./_content/StickerApp.Shared/location-surprises.js");
            enabled = await module.InvokeAsync<bool>("isEnabled");
            isReady = true;
        }
        catch (JSException)
        {
            error = "Easter egg settings could not be loaded. Try reloading the app.";
        }
        StateHasChanged();
    }

    private async Task ChangeEnabled(ChangeEventArgs args)
    {
        if (module is null) return;
        error = null;

        try
        {
            var selected = args.Value is true;
            await module.InvokeVoidAsync("setEnabled", selected);
            enabled = selected;
        }
        catch (JSException)
        {
            error = "Could not save the setting. Check that device storage is enabled.";
        }
    }

    private async Task PreviewRomania()
    {
        if (module is null) return;

        try
        {
            await module.InvokeVoidAsync("previewRomania");
        }
        catch (JSException)
        {
            error = "The preview could not be opened. Try reloading the app.";
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (module is null) return;

        try
        {
            await module.DisposeAsync();
        }
        catch (JSDisconnectedException)
        {
            // The app can close before its browser connection finishes shutting down.
        }
    }
}
