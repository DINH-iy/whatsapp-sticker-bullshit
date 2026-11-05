using Foundation;
using ObjCRuntime;
using StickerApp.Core;
using UIKit;

namespace StickerApp.ShareExtension;

[Register("PinterestShareViewController")]
public sealed class PinterestShareViewController : UIViewController
{
    private UILabel message = null!;
    private UIButton save = null!;
    private string? sharedUrl;
    private bool closed;

    public PinterestShareViewController(NativeHandle handle) : base(handle) { }

    public override void ViewDidLoad()
    {
        base.ViewDidLoad();
        var view = View!;
        view.BackgroundColor = UIColor.SystemBackground;
        var title = new UILabel { Text = "Save Pinterest selection", Font = UIFont.BoldSystemFontOfSize(22)!, Lines = 0 };
        message = new UILabel { Text = "Reading the shared link…", Lines = 0 };
        save = UIButton.FromType(UIButtonType.System);
        save.SetTitle("Save link for Stickers", UIControlState.Normal);
        save.Enabled = false;
        save.TouchUpInside += Save;
        var cancel = UIButton.FromType(UIButtonType.System);
        cancel.SetTitle("Cancel", UIControlState.Normal);
        cancel.TouchUpInside += (_, _) => Complete();
        var stack = new UIStackView([title, message, save, cancel])
        {
            Axis = UILayoutConstraintAxis.Vertical, Spacing = 20,
            TranslatesAutoresizingMaskIntoConstraints = false
        };
        view.AddSubview(stack);
        NSLayoutConstraint.ActivateConstraints([
            stack.LeadingAnchor.ConstraintEqualTo(view.SafeAreaLayoutGuide.LeadingAnchor, 24),
            stack.TrailingAnchor.ConstraintEqualTo(view.SafeAreaLayoutGuide.TrailingAnchor, -24),
            stack.TopAnchor.ConstraintEqualTo(view.SafeAreaLayoutGuide.TopAnchor, 24)
        ]);
        _ = ReadLink();
    }

    private async Task ReadLink()
    {
        try
        {
            foreach (var item in ExtensionContext?.InputItems.OfType<NSExtensionItem>() ?? [])
            {
                if (PinterestShareLink.TryParse(item.AttributedContentText?.Value, out var textUrl))
                {
                    sharedUrl = textUrl.AbsoluteUri;
                    break;
                }
                foreach (var provider in item.Attachments ?? [])
                {
                    if (provider.CanLoadObject(typeof(NSUrl)))
                    {
                        using var value = await provider.LoadObjectAsync<NSUrl>().WaitAsync(TimeSpan.FromSeconds(10));
                        if (PinterestShareLink.TryParse(value?.AbsoluteString, out var url)) sharedUrl = url.AbsoluteUri;
                    }
                    if (sharedUrl is null && provider.CanLoadObject(typeof(NSString)))
                    {
                        using var value = await provider.LoadObjectAsync<NSString>().WaitAsync(TimeSpan.FromSeconds(10));
                        if (PinterestShareLink.TryParse(value?.ToString(), out var url)) sharedUrl = url.AbsoluteUri;
                    }
                    if (sharedUrl is not null) break;
                }
                if (sharedUrl is not null) break;
            }
            if (closed) return;
            message.Text = sharedUrl is null
                ? "Share a public Pinterest selection link. Select pins on a board, then share the selection."
                : "Save this link, then open Stickers to preview the images and choose a pack.\n\n" + sharedUrl;
            save.Enabled = sharedUrl is not null;
        }
        catch (Exception exception) when (exception is TimeoutException or InvalidOperationException or NSErrorException)
        {
            if (!closed) message.Text = "Could not read this share. Try sharing the Pinterest selection again.";
        }
    }

    private void Save(object? sender, EventArgs args)
    {
        if (sharedUrl is null) return;
        save.Enabled = false;
        try
        {
            var container = NSFileManager.DefaultManager.GetContainerUrl(PinterestShareLink.AppGroup)?.Path
                ?? throw new InvalidOperationException("The shared inbox is unavailable. Enable the App Group for Stickers and its Share Extension in the signing profiles.");
            PinterestShareInboxFiles.Save(container, sharedUrl);
            Complete();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            message.Text = exception.Message;
            save.Enabled = true;
        }
    }

    private void Complete()
    {
        closed = true;
        ExtensionContext?.CompleteRequest([], null);
    }

    protected override void Dispose(bool disposing)
    {
        closed = true;
        base.Dispose(disposing);
    }
}
