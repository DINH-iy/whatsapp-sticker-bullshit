namespace StickerApp;

public partial class MainPage : ContentPage
{
    public MainPage()
    {
        InitializeComponent();
#if ANDROID
        // Resize the WebView into the visible area when Android shows the keyboard.
        SafeAreaEdges = Microsoft.Maui.SafeAreaEdges.All;
#endif
    }
}
