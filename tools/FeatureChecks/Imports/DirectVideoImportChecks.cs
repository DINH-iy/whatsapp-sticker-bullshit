using System.Buffers.Binary;
using StickerApp.Core;
using StickerApp.Shared.Services;

internal static class DirectVideoImportChecks
{
    public static async Task<int> RunAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "stik-direct-video-" + Guid.NewGuid().ToString("N"));
        try {
            var store = new LibraryStore(root);
            var pack = await store.CreatePackAsync("Direct video stickers");
            // Header-only persistence fixture. Browser checks separately decode real encoder output.
            var still = new byte[38]; "RIFF"u8.CopyTo(still); BinaryPrimitives.WriteUInt32LittleEndian(still.AsSpan(4), 30);
            "WEBPVP8X"u8.CopyTo(still.AsSpan(8)); BinaryPrimitives.WriteUInt32LittleEndian(still.AsSpan(16), 10);
            still[24] = still[27] = 255; still[25] = still[28] = 1; "VP8L"u8.CopyTo(still.AsSpan(30));
            var bytes = MediaInspector.AsAnimatedSticker(still);
            using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
            try { await store.ImportAsync("Cancelled.webp", bytes, pack, cancellationToken: cancelled.Token); throw new Exception("Cancelled import was accepted"); }
            catch (OperationCanceledException) { }
            if ((await store.ReadAsync()).Stickers.Count != 0 || Directory.GetFiles(Path.Combine(root, "media")).Length != 0)
                throw new Exception("Cancelled video import left media or metadata behind");
            var id = await store.ImportAsync("Video sticker.webp", bytes, pack);
            var saved = (await store.ReadAsync()).Stickers.Single();
            if (saved.Id != id || saved.MimeType != "image/webp" || !saved.Animated || !saved.FileName.EndsWith(".webp") || StickerPreparationService.NeedsPreparation(saved))
                throw new Exception("Direct WebP was incorrectly queued for another conversion");
            if ((await store.ReadAsync()).Packs.Single().StickerIds.Single() != id)
                throw new Exception("Direct sticker was not added to the requested pack");
            Console.WriteLine("PASS cancelled video saving leaves no files; direct animated WebP imports ready without preparation and retains pack membership");
            return 3;
        } finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
