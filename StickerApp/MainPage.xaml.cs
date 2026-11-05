namespace StickerApp;

public partial class MainPage : ContentPage
{
    public MainPage()
    {
        InitializeComponent();
#if IOS
        BackgroundColor = Color.FromArgb(Preferences.Default.Get("native.background", "#f8f9f5"));
#endif
#if ANDROID || IOS
        // Keep the WebView inside system bars, cutouts, and the software keyboard.
        SafeAreaEdges = Microsoft.Maui.SafeAreaEdges.All;
#endif
    }

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
