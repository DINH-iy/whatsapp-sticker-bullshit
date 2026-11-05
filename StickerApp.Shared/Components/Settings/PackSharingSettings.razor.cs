using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace StickerApp.Shared.Components.Settings;

public partial class PackSharingSettings
{
    private ElementReference serverInput;
    private ElementReference tokenInput;
    private IJSObjectReference? module;
    private string serverUrl = "";
    private bool hasToken;
    private bool isReady;
    private string? notice;
    private string? error;

    public sealed class SharingSettings
    {
        public string ServerUrl { get; set; } = "";
        public bool HasToken { get; set; }
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender) return;
        try
        {
            module = await JS.InvokeAsync<IJSObjectReference>("import", "./_content/StickerApp.Shared/js/ui/pack-sharing.js");
            await ReloadSettings();
            isReady = true;
        }
        catch (JSException)
        {
            error = "Sharing settings could not be loaded. Reload the app to try again.";
        }
        StateHasChanged();
    }

    private async Task ReloadSettings()
    {
        if (module is null) return;
        var settings = await module.InvokeAsync<SharingSettings>("getSettings");
        serverUrl = settings.ServerUrl;
        hasToken = settings.HasToken;
    }

    private async Task SaveSettings()
    {
        if (module is null) return;
        error = notice = null;
        try
        {
            await module.InvokeVoidAsync("saveSettings", serverInput, tokenInput);
            await ReloadSettings();
            notice = "Sharing settings saved. Select a pack and use Share pack to create a link.";
        }
        catch (JSException exception)
        {
            error = exception.Message.Split('\n')[0];
        }
    }

    private async Task Disconnect()
    {
        if (module is null) return;
        error = notice = null;
        try
        {
            await module.InvokeVoidAsync("clearSettings");
            await ReloadSettings();
            notice = "Sharing server disconnected from this device. Previously published links remain available on the server.";
        }
        catch (JSException)
        {
            error = "Could not clear the sharing settings.";
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (module is null) return;
        try { await module.DisposeAsync(); }
        catch (JSDisconnectedException) { }
    }
}
