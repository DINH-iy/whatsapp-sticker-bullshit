using StickerApp.Core;

internal static class GifSizeChecks
{
    public static async Task<int> RunAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "stik-gif-size-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new LibraryStore(root);
            var small = Convert.FromBase64String("R0lGODlhAQABAIAAAAAAAP///yH5BAEAAAAALAAAAAABAAEAAAIBRAA7");
            // Padding after the trailer leaves the valid GIF unchanged for decoders.
            var boundary = new byte[LibraryStore.MaxGifImportBytes];
            small.CopyTo(boundary, 0);
            var pack = await store.CreatePackAsync("20 MiB GIF backup");
            var id = await store.ImportAsync("At the limit.gif", boundary, pack);
            await store.ReplaceImageAsync(id, boundary);
            var file = Path.Combine(root, "batch.gif");
            await File.WriteAllBytesAsync(file, boundary);
            await store.ImportBatchAsync([new("Batch GIF", file)], pack);
            using var exported = new MemoryStream();
            await store.ExportPackAsync(pack, exported);
            exported.Position = 0;
            var restored = new LibraryStore(Path.Combine(root, "restored"));
            await restored.ImportPackAsync(exported);
            if ((await restored.ReadAsync()).Stickers.Count != 2)
                throw new Exception("20 MiB GIF backup did not round-trip");
            Console.WriteLine("PASS exactly 20 MiB GIF imports, replaces, batches and round-trips through editable backup");
            var tooBig = new byte[LibraryStore.MaxGifImportBytes + 1]; small.CopyTo(tooBig, 0);
            try { await store.ImportAsync("Too big.gif", tooBig); throw new Exception("Oversized GIF accepted"); }
            catch (InvalidDataException ex) when (ex.Message == LibraryStore.GifSizeError) { }
            var png = new byte[LibraryStore.MaxImportBytes + 1];
            Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVQIHWP4z8DwHwAFgAI/ScLttAAAAABJRU5ErkJggg==").CopyTo(png, 0);
            try { await store.ImportAsync("Still.png", png); throw new Exception("Oversized still image accepted"); }
            catch (InvalidDataException) { }
            if ((await store.ReadAsync()).Stickers.Count != 2) throw new Exception("Size rejection saved partial files");
            Console.WriteLine("PASS GIF above 20 MiB gives a specific error; still images retain their 10 MiB limit");
            return 2;
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
