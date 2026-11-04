using Android.Content;
using Android.Content.Res;
using Android.Database;
using Android.OS;
using StickerApp.Services;
using System.Text.Json;
using Uri = Android.Net.Uri;

namespace StickerApp;

[ContentProvider([AndroidWhatsAppExporter.Authority], Exported = true, ReadPermission = "com.whatsapp.sticker.READ")]
public sealed class StickerContentProvider : ContentProvider
{
    private static readonly string[] MetadataColumns = ["sticker_pack_identifier", "sticker_pack_name", "sticker_pack_publisher", "sticker_pack_icon", "android_play_store_link", "ios_app_download_link", "sticker_pack_publisher_email", "sticker_pack_publisher_website", "sticker_pack_privacy_policy_website", "sticker_pack_license_agreement_website", "image_data_version", "whatsapp_will_not_cache_stickers", "animated_sticker_pack"];
    public override bool OnCreate() => true;

    private static IEnumerable<ExportedPack> ReadPacks(string? id = null)
    {
        if (!Directory.Exists(AndroidWhatsAppExporter.ExportRoot)) yield break;
        foreach (var file in Directory.EnumerateFiles(AndroidWhatsAppExporter.ExportRoot, "*.json"))
        {
            if (id is not null && Path.GetFileNameWithoutExtension(file) != id) continue;
            // Manifests are atomically replaced by the app; no library files are exposed.
            var pack = JsonSerializer.Deserialize<ExportedPack>(File.ReadAllText(file));
            if (pack is not null) yield return pack;
        }
    }

    public override ICursor? Query(Uri uri, string[]? projection, string? selection, string[]? selectionArgs, string? sortOrder)
    {
        var segments = uri.PathSegments;
        if (segments is null || segments.Count == 0) throw new Java.Lang.IllegalArgumentException("Missing path.");
        if (segments[0] == "metadata" && segments.Count <= 2)
        {
            var cursor = new MatrixCursor(MetadataColumns);
            foreach (var exported in ReadPacks(segments.Count == 2 ? segments[1] : null))
            {
                var p = exported.Pack;
                var row = cursor.NewRow() ?? throw new InvalidOperationException("Cannot create cursor row.");
                foreach (var value in new[] { p.Id.ToString("N"), p.Name, p.Publisher, "tray.png", "", "", "", "", "", "", p.Version }) row.Add(new Java.Lang.String(value));
                row.Add(Java.Lang.Integer.ValueOf(0)!); row.Add(Java.Lang.Integer.ValueOf(p.Animated ? 1 : 0)!);
            }
            return cursor;
        }
        if (segments[0] == "stickers" && segments.Count == 2)
        {
            var cursor = new MatrixCursor(new[] { "sticker_file_name", "sticker_emoji", "sticker_accessibility_text" });
            foreach (var s in ReadPacks(segments[1]).SelectMany(p => p.Stickers))
            {
                var row = cursor.NewRow() ?? throw new InvalidOperationException("Cannot create cursor row.");
                row.Add(new Java.Lang.String(s.FileName)); row.Add(new Java.Lang.String("\U0001F642")); row.Add(new Java.Lang.String(s.Name));
            }
            return cursor;
        }
        throw new Java.Lang.IllegalArgumentException("Unknown sticker URI.");
    }

    public override AssetFileDescriptor? OpenAssetFile(Uri uri, string mode)
    {
        if (mode != "r") throw new Java.Lang.SecurityException("Read access only.");
        var s = uri.PathSegments;
        if (s is null || s.Count != 3 || s[0] != "stickers_asset") return null;
        var pack = ReadPacks(s[1]).SingleOrDefault();
        if (pack is null || (s[2] != "tray.png" && !pack.Stickers.Any(sticker => sticker.FileName == s[2]))) return null;
        var path = Path.Combine(pack.Folder, s[2]);
        if (!File.Exists(path)) return null;
        var descriptor = ParcelFileDescriptor.Open(new Java.IO.File(path), ParcelFileMode.ReadOnly);
        return descriptor is null ? null : new AssetFileDescriptor(descriptor, 0, new FileInfo(path).Length);
    }

    public override string? GetType(Uri uri)
    {
        var s = uri.PathSegments;
        if (s is null || s.Count == 0) return null;
        return s[0] switch
        {
            "metadata" => $"vnd.android.cursor.{(s.Count == 1 ? "dir" : "item")}/vnd.{AndroidWhatsAppExporter.Authority}.metadata",
            "stickers" => $"vnd.android.cursor.dir/vnd.{AndroidWhatsAppExporter.Authority}.stickers",
            "stickers_asset" when s.Count == 3 => s[2] == "tray.png" ? "image/png" : "image/webp",
            _ => null
        };
    }

    public override Uri? Insert(Uri uri, ContentValues? values) => throw new Java.Lang.UnsupportedOperationException();
    public override int Delete(Uri uri, string? selection, string[]? selectionArgs) => throw new Java.Lang.UnsupportedOperationException();
    public override int Update(Uri uri, ContentValues? values, string? selection, string[]? selectionArgs) => throw new Java.Lang.UnsupportedOperationException();
}

