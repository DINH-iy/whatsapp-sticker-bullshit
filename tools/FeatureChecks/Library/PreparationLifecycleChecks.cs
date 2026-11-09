using Microsoft.JSInterop;
using StickerApp.Core;
using StickerApp.Shared.Services;

internal static class PreparationLifecycleChecks
{
    public static async Task<int> RunAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "stik-foreground-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new LibraryStore(root);
            var id = await store.ImportAsync("fixture.png", Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVQIHWP4z8DwHwAFgAI/ScLttAAAAABJRU5ErkJggg=="));
            var original = (await store.ReadAsync()).Stickers.Single().FileName;
            var js = new BlockingJs();
            await using var preparation = new StickerPreparationService(store, js, new AppLog(Path.Combine(root, "logs")));
            preparation.Start();
            await Task.Delay(30);
            Require(js.Calls == 0, "Preparation started before the app was visible");
            await preparation.SetForegroundAsync(true);
            await js.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            using (await preparation.SuspendForEditingAsync())
            {
                Require(preparation.ActiveName is null, "Editor lease did not stop preparation");
                Require((await store.ReadAsync()).Stickers.Single().FileName == original, "Cancelled preparation replaced the edited source");
                var calls = js.Calls;
                preparation.Retry(id); await Task.Delay(30);
                Require(js.Calls == calls, "Preparation ran while an editor held a lease");
                js.Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
            }
            await js.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await preparation.SetForegroundAsync(false);
            Require(preparation.ActiveName is null && preparation.GetStatus((await store.ReadAsync()).Stickers.Single()) is null, "Background cancellation left a stuck job/error");
            var stoppedCalls = js.Calls;
            preparation.Retry(id); await Task.Delay(30);
            Require(js.Calls == stoppedCalls, "Preparation continued in the background");
            using var activity = new ForegroundActivity();
            using var download = activity.BeginOperation(); activity.SetActive(false);
            Require(download.IsCancellationRequested, "Native download was not cancelled on backgrounding");
            using var hidden = activity.BeginOperation(); Require(hidden.IsCancellationRequested, "Hidden app accepted new work");
            activity.SetActive(true); using var resumed = activity.BeginOperation(); Require(!resumed.IsCancellationRequested, "Foreground work could not resume");
            Console.WriteLine("PASS preparation foreground-only operation, editor lease cancellation, no source replacement, retry/resume and native download cancellation");
            await CheckManualCancellationAsync(Path.Combine(root, "manual"));
            return 12;
        }
        finally { Directory.Delete(root, true); }
    }
    private static void Require(bool value, string message) { if (!value) throw new Exception(message); }
    private static async Task CheckManualCancellationAsync(string root)
    {
        var store = new LibraryStore(root);
        var bytes = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVQIHWP4z8DwHwAFgAI/ScLttAAAAABJRU5ErkJggg==");
        var active = await store.ImportAsync("active.png", bytes);
        var queued = await store.ImportAsync("queued.png", bytes);
        var js = new BlockingJs { IgnoreCancellation = true };
        await using var service = new StickerPreparationService(store, js, new AppLog(Path.Combine(root, "logs")));
        service.Start(); await service.SetForegroundAsync(true);
        await js.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await service.CancelAsync(queued);
        Require(service.GetStatus((await store.ReadAsync()).Stickers.Single(image => image.Id == queued))?.Cancelled == true, "Queued job could not be stopped");
        await store.DeleteStickersAsync([queued]);
        await service.CancelAsync(active).WaitAsync(TimeSpan.FromSeconds(1));
        Require(service.GetStatus((await store.ReadAsync()).Stickers.Single())?.Cancelled == true, "Active job was not marked stopped");
        // Simulate a delayed encoder reply: deletion must not wait for it.
        await store.DeleteStickersAsync([active]).WaitAsync(TimeSpan.FromSeconds(1));
        js.Release.TrySetResult();
        for (var i = 0; i < 100 && service.ActiveName is not null; i++) await Task.Delay(10);
        Require(service.ActiveName is null && (await store.ReadAsync()).Stickers.Count == 0 && js.Calls == 1,
            "Late encoding output resurrected a deleted sticker or processed a deleted queued item");
        Console.WriteLine("PASS active and queued stickers stop/delete without waiting for a late encoder result; deleted stickers stay deleted");

        js.IgnoreCancellation = false; js.Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var resumable = await store.ImportAsync("resumable.png", bytes);
        await js.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await service.CancelAsync(resumable);
        await service.SetForegroundAsync(false); await service.SetForegroundAsync(true);
        var calls = js.Calls; await Task.Delay(50);
        Require(js.Calls == calls && service.GetStatus((await store.ReadAsync()).Stickers.Single())?.Cancelled == true,
            "Manually stopped sticker restarted when returning to the app");
        js.Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        service.Retry(resumable);
        await js.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await service.CancelAsync(resumable);
        Console.WriteLine("PASS stopped preparation remains paused across foreground changes and resumes only on request");
        using (await service.SuspendForEditingAsync())
        {
            await store.ImportAsync("queued-for-bulk-delete.png", bytes);
            js.Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
            service.Retry(resumable);
        }
        await js.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var beforeDelete = js.Calls;
        await service.DeleteStickersAsync((await store.ReadAsync()).Stickers.Select(image => image.Id));
        for (var i = 0; i < 100 && service.ActiveName is not null; i++) await Task.Delay(10);
        Require((await store.ReadAsync()).Stickers.Count == 0 && service.ActiveName is null && js.Calls == beforeDelete,
            "Bulk deletion left a worker active or started a queued selected sticker");
        Console.WriteLine("PASS bulk deletion stops active preparation and removes queued stickers without starting them");
    }
    private sealed class BlockingJs : IJSRuntime, IJSObjectReference
    {
        public int Calls;
        public bool IgnoreCancellation;
        public TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ValueTask<T> InvokeAsync<T>(string name, object?[]? args) => InvokeAsync<T>(name, default, args);
        public async ValueTask<T> InvokeAsync<T>(string name, CancellationToken token, object?[]? args)
        {
            if (name == "import") return (T)(object)this;
            if (name == "prepare")
            {
                ++Calls; Started.TrySetResult();
                if (IgnoreCancellation) { await Release.Task; return (T)(object)"data:image/webp;base64,ignored"; }
                await Task.Delay(Timeout.Infinite, token);
            }
            return default!;
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
