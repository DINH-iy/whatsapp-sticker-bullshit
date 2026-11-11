using Microsoft.JSInterop;
using StickerApp.Core;

namespace StickerApp.Shared.Services;

public static class PackImportMedia
{
    public static async Task<byte[]> NormalizeAsync(IJSRuntime js, byte[] bytes)
    {
        // GIF metadata lacks frame timings; only WebP wrappers need classification here.
        if (MediaInspector.Inspect(bytes).MimeType != "image/webp")
            return bytes;
        await using var module = await js.InvokeAsync<IJSObjectReference>("import", "./_content/StickerApp.Shared/js/media/animation/classify-sticker.js");
        var still = await module.InvokeAsync<string?>("stillFrame", "data:image/webp;base64," + Convert.ToBase64String(bytes));
        return still is null ? bytes : Convert.FromBase64String(still[(still.IndexOf(',') + 1)..]);
    }

    public static string PickerAccept(object? formFactor)
    {
        return ".wastickers,.wasstickers";
    }
}
