using StickerApp.Core;
using StickerApp.Shared.Services;

namespace StickerApp.Services;

public sealed class NativeAppRestart(AppLog log) : IAppRestart
{
    public Task RestartAsync() => MainThread.InvokeOnMainThreadAsync(() =>
    {
        var application = Application.Current
            ?? throw new InvalidOperationException("The native application is unavailable. Close and reopen the app to finish resetting.");
        var windows = application.Windows.ToArray();
        if (windows.Length == 0)
            throw new InvalidOperationException("The native app window is unavailable. Close and reopen the app to finish resetting.");

        // Construct all replacement hosts before detaching any existing UI.
        var replacements = windows.Select(window => (Window: window, Previous: window.Page, Next: new MainPage())).ToList();
        foreach (var item in replacements) item.Window.Page = item.Next;

        // Disconnect through MAUI's handler lifecycle. Android disposes the old
        // renderer asynchronously, avoiding a wait on its in-flight reset event.
        foreach (var item in replacements)
            if (item.Previous is MainPage previous)
            {
                try { previous.DisconnectBlazorHost(); }
                catch (Exception exception) { log.Error("Disconnect previous native host after factory reset", exception); }
            }
    });
}
