using Foundation;
using UIKit;

namespace StickerApp;

[Register("SceneDelegate")]
public class SceneDelegate : MauiUISceneDelegate
{
    public override void WillConnect(UIScene scene, UISceneSession session, UISceneConnectionOptions connectionOptions)
    {
        if (connectionOptions.UrlContexts is { } contexts)
            foreach (var context in contexts) StickerApp.Core.GroupInviteInbox.Receive(context.Url.AbsoluteString);
        base.WillConnect(scene, session, connectionOptions);
    }

}
