namespace StickerApp;

public partial class App : Application
{
    private readonly StickerApp.Shared.Services.PinterestShareState shares;
    private readonly StickerApp.Shared.Services.ForegroundActivity activity;

    public App(StickerApp.Shared.Services.PinterestShareState shares, StickerApp.Shared.Services.ForegroundActivity activity)
    {
        this.shares = shares;
        this.activity = activity;
        InitializeComponent();
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var page = new MainPage();
        var window = new Window(page) { Title = "Stik-er-in" };
        window.Stopped += (_, _) => { activity.SetActive(false); page.SetForeground(false); };
        window.Resumed += (_, _) => { activity.SetActive(true); page.SetForeground(true); };
        window.Destroying += (_, _) => { activity.SetActive(false); page.SetForeground(false); };
        window.Resumed += (_, _) => shares.CheckInbox();
        return window;
    }
}

