using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platform;
using UIKit;

namespace StickerApp.Services;

public sealed class ThemedPageHandler : PageHandler
{
    protected override Microsoft.Maui.Platform.ContentView CreatePlatformView()
    {
        if (VirtualView is null || MauiContext is null) throw new InvalidOperationException("Page context is unavailable.");
        ViewController ??= new ThemedPageController(VirtualView, MauiContext);
        if (ViewController is PageViewController page && page.CurrentPlatformView is Microsoft.Maui.Platform.ContentView content) return content;
        return ViewController.View as Microsoft.Maui.Platform.ContentView ?? throw new InvalidOperationException("Page view is unavailable.");
    }
}

public sealed class ThemedPageController(IView page, IMauiContext context) : PageViewController(page, context)
{
    public bool LightIcons { get; set; } = Preferences.Default.Get("native.dark", false);
    public override UIStatusBarStyle PreferredStatusBarStyle() => LightIcons ? UIStatusBarStyle.LightContent : UIStatusBarStyle.DarkContent;
}
