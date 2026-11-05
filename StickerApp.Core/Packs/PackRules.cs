namespace StickerApp.Core;

public static class PackRules
{
    public sealed record Issue(string Message, Guid? StickerId = null, string? Action = null);

    public static IReadOnlyList<string> Validate(StickerPack pack, IReadOnlyList<Sticker> stickers) =>
        Inspect(pack, stickers).Select(issue => issue.Message).ToList();

    public static IReadOnlyList<Issue> Inspect(StickerPack pack, IReadOnlyList<Sticker> stickers)
    {
        List<Issue> issues = [];
        if (string.IsNullOrWhiteSpace(pack.Name) || pack.Name.Length > 128)
            issues.Add(new("Give this pack a name of 1–128 characters using Rename."));
        if (stickers.Count < 3)
            issues.Add(new($"Add {3 - stickers.Count} more {(3 - stickers.Count == 1 ? "sticker" : "stickers")} using the Add sticker tile. WhatsApp needs at least 3 per pack."));
        if (stickers.Count > 30)
            issues.Add(new($"Remove {stickers.Count - 30} stickers from this pack. WhatsApp allows at most 30."));
        if (stickers.Count != pack.StickerIds.Count || stickers.Select(s => s.Id).Distinct().Count() != stickers.Count)
            issues.Add(new("Some stickers are missing or repeated. Remove those entries and add the images again."));
        foreach (var image in stickers)
        {
            var limitKb = image.Animated ? 500 : 100;
            List<string> changes = [];
            if (image.MimeType != "image/webp") changes.Add("convert to WebP (WhatsApp's sticker format)");
            if (image.Width != 512 || image.Height != 512)
                changes.Add($"fit {image.Width} × {image.Height} into a 512 × 512 pixel square, keeping its proportions with transparent padding");
            if (image.Bytes > limitKb * 1024)
                changes.Add($"compress from {Math.Ceiling(image.Bytes / 1024d):0} KB to at most {limitKb} KB");
            if (image.Bytes <= 0)
                issues.Add(new($"{image.Name}: this file is empty. Open Edit and replace it with the original image.", image.Id, "Edit / replace"));
            else if (changes.Count > 0)
                issues.Add(new($"{image.Name}: {string.Join("; ", changes)}. Automatic preparation is pending; open sticker details for its status.", image.Id, "Details"));
            if (image.Animated && image.MimeType == "image/webp" &&
                (image.DurationMs <= 0 || image.DurationMs > 10_000 || image.MinimumFrameMs < 8))
                issues.Add(new($"{image.Name}: animation is {image.DurationMs / 1000d:0.##} seconds; WhatsApp allows up to 10 seconds and at least 8 ms per frame. Replace it with a shorter GIF, then trim and save it. Preparation runs automatically.", image.Id, "Edit / replace"));
        }
        return issues;
    }
}
