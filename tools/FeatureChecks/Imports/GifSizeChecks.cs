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
            catch (InvalidDataException ex) when (ex.Message == LibraryStore.GifLimitError(store.GifImportLimitBytes, store.DecimalStorageUnits)) { }
            var png = new byte[LibraryStore.MaxImportBytes + 1];
            Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVQIHWP4z8DwHwAFgAI/ScLttAAAAABJRU5ErkJggg==").CopyTo(png, 0);
            try { await store.ImportAsync("Still.png", png); throw new Exception("Oversized still image accepted"); }
            catch (InvalidDataException) { }
            if ((await store.ReadAsync()).Stickers.Count != 2) throw new Exception("Size rejection saved partial files");
            Console.WriteLine("PASS GIF above 20 MiB gives a specific error; still images retain their 10 MiB limit");
            await store.SetGifImportLimitAsync(50);
            var reloaded = new LibraryStore(root);
            if (reloaded.GifImportLimitBytes != 50 * 1024 * 1024) throw new Exception("GIF setting was not persisted");
            await reloaded.ImportAsync("Larger.gif", tooBig, pack);
            using var largerBackup = new MemoryStream();
            await reloaded.ExportPackAsync(pack, largerBackup);
            largerBackup.Position = 0;
            var largerReceiver = new LibraryStore(Path.Combine(root, "larger-receiver"));
            await largerReceiver.SetGifImportLimitAsync(50);
            await largerReceiver.ImportPackAsync(largerBackup);
            if ((await largerReceiver.ReadAsync()).Stickers.Count != 3) throw new Exception("Larger GIF backup did not round-trip");
            await reloaded.SetGifImportLimitAsync(5);
            try { await reloaded.ImportAsync("Over chosen limit.gif", boundary); throw new Exception("Chosen GIF limit ignored"); }
            catch (InvalidDataException ex) when (ex.Message.Contains("5.24 MB")) { }
            try { await reloaded.SetGifImportLimitAsync(101); throw new Exception("Invalid limit accepted"); }
            catch (ArgumentOutOfRangeException) { }
            if ((await reloaded.ReadAsync()).Stickers.Count != 3) throw new Exception("Configured limit rejection changed the library");
            Console.WriteLine("PASS GIF limit persists, accepts larger GIFs, rejects above chosen limit and validates range");
            await reloaded.SetGifImportLimitBytesAsync(50_000_000);
            await reloaded.SetDecimalStorageUnitsAsync(false);
            if (reloaded.GifImportLimitBytes != 50_000_000) throw new Exception("Switching units changed the GIF limit");
            var exact = new LibraryStore(root);
            if (exact.GifImportLimitBytes != 50_000_000 || exact.DecimalStorageUnits) throw new Exception("Exact GIF byte limit or selected units did not persist");
            if (!LibraryStore.GifLimitError(50_000_000, true).Contains("50 MB") || !LibraryStore.GifLimitError(50_000_000, false).Contains("47.68 MiB")) throw new Exception("GIF errors did not convert display units");
            Console.WriteLine("PASS GIF byte limit and units persist independently with matching error units");
            return 4;
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
