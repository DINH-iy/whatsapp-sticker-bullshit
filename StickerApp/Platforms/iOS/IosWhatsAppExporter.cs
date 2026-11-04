using CoreGraphics;
using Foundation;
using ImageIO;
using StickerApp.Core;
using System.Text.Json;
using UIKit;

namespace StickerApp.Services;

public sealed class IosWhatsAppExporter(LibraryStore store) : IWhatsAppExporter
{
    private const string PasteboardType = "net.whatsapp.third-party.sticker-pack";
    public bool IsAvailable => true;

    public async Task ExportAsync(StickerPack pack, IReadOnlyList<Sticker> stickers)
    {
        var errors = PackRules.Validate(pack, stickers);
        if (errors.Count > 0)
            throw new InvalidOperationException(string.Join(" ", errors));

        var images = new List<byte[]>(stickers.Count);
        foreach (var sticker in stickers)
        {
            var bytes = await File.ReadAllBytesAsync(store.MediaPath(sticker.FileName));
            if (MediaInspector.Inspect(bytes).MimeType != "image/webp")
                throw new InvalidOperationException("Convert all stickers to WebP first.");
            images.Add(bytes);
        }

        await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            using var url = new NSUrl("whatsapp://stickerPack");
            if (!OperatingSystem.IsIOSVersionAtLeast(27) && !UIApplication.SharedApplication.CanOpenUrl(url))
                throw new InvalidOperationException("Install or update WhatsApp to import sticker packs.");

            var payload = JsonSerializer.SerializeToUtf8Bytes(new
            {
                identifier = pack.Id.ToString("N"),
                name = pack.Name,
                publisher = "Sticker Studio",
                animated_sticker_pack = pack.Animated,
                tray_image = Convert.ToBase64String(CreateTrayIcon(pack.CoverImage is null ? images[0] : PackCover.ReadBytes(pack.CoverImage))),
                stickers = images.Select(bytes => new
                {
                    image_data = Convert.ToBase64String(bytes),
                    emojis = Array.Empty<string>()
                })
            });

            // WhatsApp reads this documented pasteboard type after opening its URL.
            // Keep the transfer local to this device and expire it after one minute.
            using var data = NSData.FromArray(payload);
            using var key = new NSString(PasteboardType);
            using var item = new NSDictionary<NSString, NSObject>(key, data);
            UIPasteboard.General.SetItems([item], new UIPasteboardOptions
            {
                LocalOnly = true,
                ExpirationDate = NSDate.FromTimeIntervalSinceNow(60)
            });

            if (!await UIApplication.SharedApplication.OpenUrlAsync(url, new UIApplicationOpenUrlOptions()))
                throw new InvalidOperationException("Install or update WhatsApp, then try exporting the pack again.");
        });
    }

    private static byte[] CreateTrayIcon(byte[] firstSticker)
    {
        // ImageIO decodes WebP, including the first frame of an animated sticker.
        using var data = NSData.FromArray(firstSticker);
        using var source = CGImageSource.FromData(data);
        using var frame = source?.CreateImage(0, new CGImageOptions())
            ?? throw new InvalidOperationException("iOS could not read the first sticker for the pack icon.");
        using var image = UIImage.FromImage(frame);
        using var format = new UIGraphicsImageRendererFormat { Scale = 1, Opaque = false };
        using var renderer = new UIGraphicsImageRenderer(new CGSize(96, 96), format);
        using var png = renderer.CreatePng(_ => image.Draw(new CGRect(0, 0, 96, 96)));
        if (png.Length > 50 * 1024)
            throw new InvalidOperationException("The pack icon is too large. Choose a simpler first sticker.");
        return png.ToArray();
    }
}
