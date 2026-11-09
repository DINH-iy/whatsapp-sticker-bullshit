namespace StickerApp.Shared.Services;

// Native hosts must recreate their BlazorWebView; reloading the HTML document
// alone does not reattach the native root components.
public interface IAppRestart
{
    Task RestartAsync();
}
