using Android.Graphics;
using Android.Database;
using Android.Widget;
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
        var animatedPack = stickers.Any(sticker => sticker.Animated);
        // Keep immutable snapshots so WhatsApp never reads a half-edited pack.
        var folder = Path.Combine(ExportRoot, pack.Id.ToString("N"), pack.Version);
        Directory.CreateDirectory(folder);
        foreach (var s in stickers)
        {
            var bytes = await File.ReadAllBytesAsync(store.MediaPath(s.FileName));
            var info = MediaInspector.Inspect(bytes);
            if (info.MimeType != "image/webp") throw new InvalidOperationException("Convert all stickers to WebP first.");
            if (animatedPack && !info.Animated)
                bytes = MediaInspector.AsSingleFrameAnimation(bytes);
            await File.WriteAllBytesAsync(Path.Combine(folder, s.FileName), bytes);
        }
        var coverBytes = pack.CoverImage is null ? null : PackCover.ReadBytes(pack.CoverImage);
        using var image = (coverBytes is null
            ? await BitmapFactory.DecodeFileAsync(store.MediaPath(stickers[0].FileName))
            : await BitmapFactory.DecodeByteArrayAsync(coverBytes, 0, coverBytes.Length))
            ?? throw new InvalidOperationException("Android could not decode the first sticker for the pack icon.");
        using var icon = Bitmap.CreateScaledBitmap(image, 96, 96, true);
        using var output = new MemoryStream();
        icon.Compress(Bitmap.CompressFormat.Png!, 100, output);
        if (output.Length > 50 * 1024) throw new InvalidOperationException("The pack icon is too large. Choose a simpler first sticker.");
        await File.WriteAllBytesAsync(Path.Combine(folder, "tray.png"), output.ToArray());
        var manifest = Path.Combine(ExportRoot, pack.Id.ToString("N") + ".json");
        var exportedPack = new StickerPack
        {
            Id = pack.Id,
            Name = pack.Name,
            Publisher = pack.Publisher,
            Animated = animatedPack,
            Version = pack.Version,
            StickerIds = pack.StickerIds.ToList(),
            CoverImage = pack.CoverImage
        };
        await File.WriteAllTextAsync(manifest + ".tmp", JsonSerializer.Serialize(new ExportedPack(exportedPack, stickers.ToList(), folder)));
        File.Move(manifest + ".tmp", manifest, true);
        var result = await global::StickerApp.MainActivity.AddStickerPackAsync(pack.Id.ToString("N"), Authority, pack.Name);
        if (!result)
            throw new InvalidOperationException("WhatsApp did not add the sticker pack.");

        var added = await MainThread.InvokeOnMainThreadAsync(() => IsWhitelisted(pack.Id.ToString("N")));
        if (!added)
            throw new InvalidOperationException("WhatsApp did not confirm that the sticker pack was added.");

        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            var resolver = global::Android.App.Application.Context.ContentResolver;
            resolver?.NotifyChange(global::Android.Net.Uri.Parse($"content://{Authority}/metadata"), null);
            resolver?.NotifyChange(global::Android.Net.Uri.Parse($"content://{Authority}/metadata/{pack.Id:N}"), null);
            resolver?.NotifyChange(global::Android.Net.Uri.Parse($"content://{Authority}/stickers/{pack.Id:N}"), null);
            Toast.MakeText(global::Android.App.Application.Context, "Added to WhatsApp", ToastLength.Short)?.Show();
        });
    }

    private static bool IsWhitelisted(string identifier)
    {
        var resolver = global::Android.App.Application.Context.ContentResolver
            ?? throw new InvalidOperationException("Android content services are unavailable.");

        foreach (var package in new[] { "com.whatsapp", "com.whatsapp.w4b" })
        {
            var uri = global::Android.Net.Uri.Parse(
                $"content://{package}.provider.sticker_whitelist_check/is_whitelisted" +
                $"?authority='{Authority}'&identifier='{identifier}'");
            using var cursor = resolver.Query(uri, null, null, null, null);
            if (cursor is null || !cursor.MoveToFirst())
                continue;
            var resultColumn = cursor.GetColumnIndex("result");
            if (resultColumn >= 0 && cursor.GetInt(resultColumn) == 1)
                return true;
        }

        return false;
    }
}
