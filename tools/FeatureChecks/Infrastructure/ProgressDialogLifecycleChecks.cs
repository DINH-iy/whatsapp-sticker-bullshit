using System.Reflection;
using Microsoft.JSInterop;
using StickerApp.Shared.Components.Common;
using StickerApp.Shared.Services;

internal static class ProgressDialogLifecycleChecks
{
    public static async Task<int> RunAsync()
    {
        foreach (var stage in new[] { "import", "open", "watchElapsed", "none" })
        {
            var js = new DelayedJs(stage);
            var component = new OperationProgressDialog();
            Set(component, "JS", js);
            Set(component, "L", new AppLanguage());
            var initialize = (Task)typeof(OperationProgressDialog).GetMethod("OnAfterRenderAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(component, [true])!;
            if (stage != "none")
            {
                await js.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
                await component.DisposeAsync();
                // The pending interop call must retain ownership of its module.
                if (js.Disposals != 0) throw new Exception("Overlay disposed its module during initialization: " + stage);
                js.Release.SetResult();
            }
            await initialize.WaitAsync(TimeSpan.FromSeconds(5));
            await component.DisposeAsync();
            if (js.Disposals != 1) throw new Exception("Overlay module was not released exactly once: " + stage);
            var expected = stage == "import" ? "import" : stage == "open" ? "import,open" : "import,open,watchElapsed";
            if (string.Join(',', js.Calls) != expected) throw new Exception("Removed overlay continued initialization: " + stage);
            Console.WriteLine("PASS loading overlay completes safely when removed during " + stage);
        }
        return 4;
    }

    private static void Set(object component, string property, object value)
        => component.GetType().GetProperty(property, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(component, value);

    private sealed class DelayedJs(string pauseAt) : IJSRuntime, IJSObjectReference
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public List<string> Calls { get; } = [];
        public int Disposals { get; private set; }
        public ValueTask<T> InvokeAsync<T>(string identifier, object?[]? args) => InvokeAsync<T>(identifier, default, args);
        public async ValueTask<T> InvokeAsync<T>(string identifier, CancellationToken token, object?[]? args)
        {
            ObjectDisposedException.ThrowIf(Disposals != 0, this);
            Calls.Add(identifier);
            if (identifier == pauseAt) { Entered.SetResult(); await Release.Task; }
            ObjectDisposedException.ThrowIf(Disposals != 0, this);
            return identifier == "import" ? (T)(object)this : default!;
        }
        public ValueTask DisposeAsync() { Disposals++; return ValueTask.CompletedTask; }
    }
}
