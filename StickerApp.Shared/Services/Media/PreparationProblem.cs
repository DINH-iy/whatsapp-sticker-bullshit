using StickerApp.Core;

namespace StickerApp.Shared.Services;

public sealed record PreparationProblem(string Summary, string Recovery, string EditLabel)
{
    public static PreparationProblem Describe(Sticker image, string error)
    {
        var text = error.ToLowerInvariant();
        if (text.Contains("500 kb") || text.Contains("100 kb") || text.Contains("size limit"))
            return new(image.Animated ? "Animation could not fit within the 500 KB sticker limit." : "Image could not fit within the 100 KB sticker limit.",
                image.Animated ? "Shorten the animation in Edit, then save. For video imports, you can also use fewer frames per second."
                    : "Open Edit and crop or simplify the artwork, then save. If it still fails, replace it with a smaller image.", image.Animated ? "Shorten animation" : "Edit image");
        if (text.Contains("10 seconds") || text.Contains("longer than"))
            return new("Animation is longer than the 10-second sticker limit.", "Open Edit, move the start and end handles to select at most 10 seconds, then save.", "Edit and trim animation");
        if (text.Contains("too large to edit") || text.Contains("too many") || text.Contains("20 mib") || text.Contains("maximum file"))
            return new("This animation exceeds the editor's size or frame limit.", "For a video, import it again with a shorter clip or fewer frames per second. Otherwise, shorten or resize the source in another app and import it again.", "Open editor");
        if (text.Contains("original gif") || text.Contains("original animation"))
            return new("The source animation is needed to prepare this sticker.", "Import the original GIF again, or open Edit and replace the source with that GIF.", "Edit and replace source");
        if (text.Contains("too long") || text.Contains("timed out") || text.Contains("timeout"))
            return new("Sticker preparation took too long.", "Keep the app open and try again. If it happens again, shorten the animation or replace it with a smaller source.", "Edit image");
        if (text.Contains("transparent"))
            return new("The edited sticker is completely transparent.", "Open Edit, undo the cutout or increase opacity, then save.", "Edit image");
        return new("This sticker could not be prepared.", "Try again with the app open. If it still fails, open Edit and replace the source, or contact the developer with the diagnostic log.", "Edit image");
    }
}
