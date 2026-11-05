using StickerApp.Shared.Services;
using Microsoft.Maui.Platform;
using UIKit;

namespace StickerApp.Services;

public sealed class IosNativeAppearance : INativeAppearance
{
    public Task ApplyAsync(string background, bool dark) => MainThread.InvokeOnMainThreadAsync(() =>
    {
        if (background.Length != 7 || background[0] != '#' || !background.AsSpan(1).ToString().All(Uri.IsHexDigit)) return;
        var color = Color.FromArgb(background);
        Preferences.Default.Set("native.background", background);
        Preferences.Default.Set("native.dark", dark);
        if (Application.Current is not { } app) return;
        foreach (var window in app.Windows)
        {
            if (window.Page is not { } page) continue;
            page.BackgroundColor = color;
            if (window.Handler?.PlatformView is UIWindow nativeWindow) nativeWindow.BackgroundColor = color.ToPlatform();
            if (page.Handler is Microsoft.Maui.IPlatformViewHandler handler && handler.ViewController is ThemedPageController controller)
            {
                controller.LightIcons = dark;
                controller.SetNeedsStatusBarAppearanceUpdate();
            }
        }
    });
}
