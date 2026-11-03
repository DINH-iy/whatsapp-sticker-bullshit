using Android.Content;
using Android.Graphics;
using Path = System.IO.Path;
using StickerApp.Core;
using System.Text.Json;

namespace StickerApp.Services;

public sealed record ExportedPack(StickerPack Pack, List<Sticker> Stickers, string Folder);

public sealed class AndroidWhatsAppExporter(LibraryStore store) : IWhatsAppExporter
{
    public const string Authority = "com.stickerstudio.app.stickers";
    public bool IsAvailable => true;
    internal static string ExportRoot => Path.Combine(FileSystem.AppDataDirectory, "exports");

    public async Task ExportAsync(StickerPack pack, IReadOnlyList<Sticker> stickers)
    {
        var errors = PackRules.Validate(pack, stickers);
        if (errors.Count > 0) throw new InvalidOperationException(string.Join(" ", errors));
        // Keep immutable snapshots so WhatsApp never reads a half-edited pack.
        var folder = Path.Combine(ExportRoot, pack.Id.ToString("N"), pack.Version);
        Directory.CreateDirectory(folder);
        foreach (var s in stickers)
        {
            var bytes = await File.ReadAllBytesAsync(store.MediaPath(s.FileName));
            var info = MediaInspector.Inspect(bytes);
            if (info.MimeType != "image/webp") throw new InvalidOperationException("Convert all stickers to WebP first.");
            await File.WriteAllBytesAsync(Path.Combine(folder, s.FileName), bytes);
        }
        using var image = await BitmapFactory.DecodeFileAsync(store.MediaPath(stickers[0].FileName))
            ?? throw new InvalidOperationException("Android could not decode the first sticker for the pack icon.");
        using var icon = Bitmap.CreateScaledBitmap(image, 96, 96, true);
        using var output = new MemoryStream();
        icon.Compress(Bitmap.CompressFormat.Png!, 100, output);
        if (output.Length > 50 * 1024) throw new InvalidOperationException("The pack icon is too large. Choose a simpler first sticker.");
        await File.WriteAllBytesAsync(Path.Combine(folder, "tray.png"), output.ToArray());
        var manifest = Path.Combine(ExportRoot, pack.Id.ToString("N") + ".json");
        await File.WriteAllTextAsync(manifest + ".tmp", JsonSerializer.Serialize(new ExportedPack(pack, stickers.ToList(), folder)));
        File.Move(manifest + ".tmp", manifest, true);
        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            var intent = new Intent("com.whatsapp.intent.action.ENABLE_STICKER_PACK");
            intent.PutExtra("sticker_pack_id", pack.Id.ToString("N"));
            intent.PutExtra("sticker_pack_authority", Authority);
            intent.PutExtra("sticker_pack_name", pack.Name);
            intent.SetPackage("com.whatsapp");
            intent.AddFlags(ActivityFlags.NewTask);
            try { global::Android.App.Application.Context.StartActivity(intent); }
            catch (ActivityNotFoundException) { throw new InvalidOperationException("Install or update WhatsApp to import sticker packs."); }
        });
    }
}

