namespace StickerApp.Core;

public static class PackRules
{
    public static IReadOnlyList<string> Validate(StickerPack pack, IReadOnlyList<Sticker> stickers)
    {
        List<string> errors = [];
        if (string.IsNullOrWhiteSpace(pack.Name) || pack.Name.Length > 128)
            errors.Add("Give the pack a name of 1–128 characters.");
        if (stickers.Count is < 3 or > 30) errors.Add("A WhatsApp pack needs 3–30 stickers.");
        if (stickers.Count != pack.StickerIds.Count || stickers.Select(s => s.Id).Distinct().Count() != stickers.Count)
            errors.Add("The pack contains missing or repeated stickers.");
        foreach (var s in stickers)
        {
            if (s.Animated != pack.Animated) errors.Add($"{s.Name}: use separate packs for static and animated stickers.");
            if (s.MimeType != "image/webp") errors.Add($"{s.Name}: convert to WebP before exporting.");
            if (s.Width != 512 || s.Height != 512) errors.Add($"{s.Name}: resize to 512 × 512 pixels.");
            if (s.Bytes <= 0 || s.Bytes > (s.Animated ? 500 * 1024 : 100 * 1024)) errors.Add($"{s.Name}: file exceeds the WhatsApp size limit or is empty.");
            if (s.Animated && (s.DurationMs <= 0 || s.DurationMs > 10_000 || s.MinimumFrameMs < 8))
                errors.Add($"{s.Name}: animation must be at most 10 seconds, with frames of at least 8 ms.");
        }
        return errors;
    }
}
