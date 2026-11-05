using Foundation;
using Microsoft.Extensions.DependencyInjection;
using StickerApp.Core;
using UIKit;
using WebKit;

namespace StickerApp;

public partial class MainPage
{
    private void EnableBackGesture(WKWebView webView)
    {
        // Use the same overlay-first path as Android instead of skipping an open editor.
        var backGesture = new UIScreenEdgePanGestureRecognizer(gesture =>
        {
            if (gesture.State != UIGestureRecognizerState.Ended ||
                gesture.View is not WKWebView view || gesture.TranslationInView(view).X < 40)
                return;

            view.EvaluateJavaScript("window.stickerNavigation ? window.stickerNavigation.back(true) : false", (_, error) =>
            {
                if (error is not null)
                    Handler?.MauiContext?.Services.GetService<AppLog>()?.Error("iOS back gesture", new NSErrorException(error));
            });
        }) { Edges = UIRectEdge.Left };
        webView.AddGestureRecognizer(backGesture);
        // A regular page pan should wait to see whether this is an edge-back gesture.
        webView.ScrollView.PanGestureRecognizer.RequireGestureRecognizerToFail(backGesture);
    }
}
