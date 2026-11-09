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
            return 7;
        }
        finally { Directory.Delete(root, true); }
    }
    private static void Require(bool value, string message) { if (!value) throw new Exception(message); }
    private sealed class BlockingJs : IJSRuntime, IJSObjectReference
    {
        public int Calls;
        public TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ValueTask<T> InvokeAsync<T>(string name, object?[]? args) => InvokeAsync<T>(name, default, args);
        public async ValueTask<T> InvokeAsync<T>(string name, CancellationToken token, object?[]? args)
        {
            if (name == "import") return (T)(object)this;
            if (name == "prepare") { ++Calls; Started.TrySetResult(); await Task.Delay(Timeout.Infinite, token); }
            return default!;
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
