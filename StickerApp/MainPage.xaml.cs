namespace StickerApp;

public partial class MainPage : ContentPage
{
    public MainPage()
    {
        InitializeComponent();
#if IOS
        BackgroundColor = Color.FromArgb(Preferences.Default.Get("native.background", "#f8f9f5"));
        blazorWebView.BlazorWebViewInitialized += (_, args) => EnableBackGesture(args.WebView);
#endif
#if ANDROID || IOS
        // Keep the WebView inside system bars, cutouts, and the software keyboard.
        SafeAreaEdges = Microsoft.Maui.SafeAreaEdges.All;
#endif
    }

    private int foregroundVersion;
    internal void SetForeground(bool active)
    {
        var version = ++foregroundVersion;
        const string stop = "window.stickerSetForeground?.(false);document.querySelectorAll('video,audio').forEach(media=>media.pause());";
#if ANDROID
        if (blazorWebView.Handler?.PlatformView is Android.Webkit.WebView webView)
        {
            if (active) { webView.ResumeTimers(); webView.OnResume(); webView.EvaluateJavascript("window.stickerSetForeground?.(true)", null); }
            else webView.EvaluateJavascript(stop, new BackgroundResult(webView, () => version == foregroundVersion));
        }
#elif IOS || MACCATALYST
        if (blazorWebView.Handler?.PlatformView is WebKit.WKWebView webView)
            webView.EvaluateJavaScript(active ? "window.stickerSetForeground?.(true)" : stop, (result, error) => { });
#endif
    }
#if ANDROID
    private sealed class BackgroundResult(Android.Webkit.WebView webView, Func<bool> stillBackground) : Java.Lang.Object, Android.Webkit.IValueCallback
    {
        public void OnReceiveValue(Java.Lang.Object? value) { if (stillBackground()) { webView.OnPause(); webView.PauseTimers(); } }
    }
#endif

    internal void DisconnectBlazorHost() => blazorWebView.Handler?.DisconnectHandler();

#if ANDROID
    protected override bool OnBackButtonPressed()
    {
        if (blazorWebView.Handler?.PlatformView is Android.Webkit.WebView webView)
        {
            webView.EvaluateJavascript("window.stickerNavigation ? window.stickerNavigation.back(true) : false", new BackResult());
            return true;
        }
        return base.OnBackButtonPressed();
    }
    private sealed class BackResult : Java.Lang.Object, Android.Webkit.IValueCallback
    {
        public void OnReceiveValue(Java.Lang.Object? value)
        {
            if (value?.ToString() != "true") Platform.CurrentActivity?.MoveTaskToBack(true);
        }
    }
#endif
}
