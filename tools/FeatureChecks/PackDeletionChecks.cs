using StickerApp.Core;
using StickerApp.Shared.Services;

internal static class PackDeletionChecks
{
    private static readonly byte[] Png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVQIHWP4z8DwHwAFgAI/ScLttAAAAABJRU5ErkJggg==");
    public static async Task<int> RunAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "stik-pack-deletion-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new LibraryStore(root);
            var pack = await store.CreatePackAsync("Delete pack and stickers");
            var other = await store.CreatePackAsync("Keep this pack");
            var shared = await store.ImportAsync("Shared.png", Png, pack);
            await store.ImportAsync("Unique.png", Png, pack);
            await store.AddToPackAsync(other, [shared]);
            var unrelated = await store.ImportAsync("Unrelated.png", Png, other);
            var before = await store.ReadAsync();
            var removedFiles = before.Stickers.Where(image => image.Id != unrelated).Select(image => image.FileName).ToList();
            await store.DeletePackAsync(pack, deleteStickers: true);
            var after = await store.ReadAsync();
            Require(after.Packs.Single().Id == other && after.Packs.Single().StickerIds.SequenceEqual([unrelated]), "Shared memberships were not removed safely");
            Require(after.Stickers.Single().Id == unrelated && removedFiles.All(file => !File.Exists(store.MediaPath(file))), "Pack stickers or their media were left behind");
            Require(File.Exists(store.MediaPath(after.Stickers.Single().FileName)), "Unrelated sticker media was deleted");
            Console.WriteLine("PASS optional pack deletion removes its library stickers and shared memberships, preserving unrelated packs/files");

            var workingPack = await store.CreatePackAsync("Processing pack");
            var active = await store.ImportAsync("Working.png", Png, workingPack);
            await store.ImportAsync("Queued.png", Png, workingPack);
            var js = new DelayedWorker();
            await using var preparation = new StickerPreparationService(store, js, new AppLog(Path.Combine(root, "logs")));
            using var lease = await preparation.SuspendForEditingAsync();
            preparation.Start(); await preparation.SetForegroundAsync(true);
            // Prevent the unrelated PNG from taking the first worker slot.
            await preparation.CancelAsync(unrelated);
            lease.Dispose();
            await js.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await preparation.DeletePackAsync(workingPack, true).WaitAsync(TimeSpan.FromSeconds(1));
            Require(js.CancelCalls > 0 && (await store.ReadAsync()).Stickers.All(image => image.Id == unrelated), "Pack deletion did not cancel its active and queued work");
            js.Release.SetResult();
            for (var i = 0; i < 100 && preparation.ActiveName is not null; i++) await Task.Delay(10);
            Require(preparation.ActiveName is null && js.PrepareCalls == 1 && (await store.ReadAsync()).Stickers.Single().Id == unrelated,
                "A late result restored a pack sticker or started its deleted queued sticker");
            Console.WriteLine("PASS deleting a processing pack cancels its worker without waiting for late results; deleted jobs cannot restart");
            return 5;
        }
        finally { Directory.Delete(root, true); }
    }
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
    private sealed class DelayedWorker : Microsoft.JSInterop.IJSRuntime, Microsoft.JSInterop.IJSObjectReference
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int CancelCalls, PrepareCalls;
        public ValueTask<T> InvokeAsync<T>(string name, object?[]? args) => InvokeAsync<T>(name, default, args);
        public async ValueTask<T> InvokeAsync<T>(string name, CancellationToken token, object?[]? args)
        {
            if (name == "import") return (T)(object)this;
            if (name == "cancel") CancelCalls++;
            if (name == "prepare") { PrepareCalls++; Started.TrySetResult(); await Release.Task; return (T)(object)"data:image/webp;base64,late"; }
            return default!;
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
