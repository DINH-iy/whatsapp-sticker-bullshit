using Microsoft.JSInterop;

namespace StickerApp.Shared.Components;

public partial class PinterestSettings
{
    private const string StorageKey = "stickers.pinterestBackend";
    private string backendUrl = "";
    private bool ready;
    private string? notice;
    private string? error;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender)
            return;
        try
        {
            backendUrl = await JS.InvokeAsync<string>("localStorage.getItem", StorageKey) ?? "";
            ready = true;
        }
        catch (JSException)
        {
            error = "Pinterest settings could not be loaded.";
        }
        StateHasChanged();
    }

    private async Task Save()
    {
        var value = backendUrl.Trim().TrimEnd('/');
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http"))
        {
            error = "Enter a valid backend URL.";
            return;
        }
        try
        {
            await JS.InvokeVoidAsync("localStorage.setItem", StorageKey, value);
            backendUrl = value;
            notice = "Pinterest backend saved.";
            error = null;
        }
        catch (JSException)
        {
            error = "Could not save the Pinterest backend URL.";
        }
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
