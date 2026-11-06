namespace StickerApp;

public partial class App : Application
{
    private readonly StickerApp.Shared.Services.PinterestShareState shares;

    public App(StickerApp.Shared.Services.PinterestShareState shares)
    {
        this.shares = shares;
        InitializeComponent();
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var window = new Window(new MainPage()) { Title = "Stik-er-in" };
        window.Resumed += (_, _) => shares.CheckInbox();
        return window;
    }
}

