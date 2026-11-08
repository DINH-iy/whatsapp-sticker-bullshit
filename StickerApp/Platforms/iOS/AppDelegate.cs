using Foundation;

namespace StickerApp;

[Register("AppDelegate")]
public class AppDelegate : MauiUIApplicationDelegate
{
    public override bool OpenUrl(UIKit.UIApplication app, NSUrl url, NSDictionary options) => StickerApp.Core.GroupInviteInbox.Receive(url.AbsoluteString) || base.OpenUrl(app,url,options);

    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}
