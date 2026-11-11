using System.Net;
using System.Reflection;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.RenderTree;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using StickerApp.Core;
using StickerApp.Shared.Pages.Gifs;
using StickerApp.Shared.Services;

internal static class GifImportRecoveryChecks
{
    public static async Task<int> RunAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "stik-gif-recovery-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new LibraryStore(root);
            var log = new AppLog(Path.Combine(root, "logs"));
            await using var services = new ServiceCollection().AddLogging().AddSingleton(store).AddSingleton(log)
                .AddSingleton<AppLanguage>().AddSingleton<ConnectionState>().AddSingleton<ForegroundActivity>()
                .AddSingleton<IFormFactor, NativeFormFactor>().AddSingleton<IJSRuntime, FakeJs>().BuildServiceProvider();
            await using var renderer = new ImportRenderer(services);
            var component = await renderer.MountAsync();
            foreach (var mode in new[] { "oversized", "invalid", "unexpected" })
            {
                using var client = new HttpClient(new ImportHandler(mode));
                Set(component, "nativeDownloadClient", client);
                var gif = new GifSearch.GifResult { Title = "kanker", ShareUrl = "https://static.klipy.com/ii/c3a19a0b747a76e98651f2b9a3cca5ff/9c/db/xWbV5hZR.gif" };
                await renderer.Dispatcher.InvokeAsync(() => Invoke(component, "ImportGif", gif));
                if (Get(component, "importError") is not string { Length: > 0 } || Get(component, "importCancellation") is not null)
                    throw new Exception("Failed GIF import did not recover its UI state: " + mode);
                if (mode == "oversized" && !(Get(component, "importError") as string)!.Contains("20.97 MB"))
                    throw new Exception("Oversized GIF did not explain the size limit");
                if (renderer.Errors.Count != 0 || (await store.ReadAsync()).Stickers.Count != 0)
                    throw new Exception("Failed GIF import escaped to the renderer or saved a sticker");
                // A second attempt on the same mounted component must still be handled.
                await renderer.Dispatcher.InvokeAsync(() => Invoke(component, "ImportGif", gif));
                if (Get(component, "importCancellation") is not null || renderer.Errors.Count != 0)
                    throw new Exception("Retry left GIF import stuck");
                Console.WriteLine("PASS native GIF popup handles failure and retry without renderer teardown: " + mode);
            }
            if (Environment.GetEnvironmentVariable("STIK_KLIPY_GIF_PATH") is { } gifPath)
            {
                var original = await File.ReadAllBytesAsync(gifPath);
                using var actualClient = new HttpClient(new ActualGifHandler(original));
                Set(component, "nativeDownloadClient", actualClient);
                await renderer.Dispatcher.InvokeAsync(() => Invoke(component, "ImportGif", new GifSearch.GifResult
                {
                    Title = "kanker", ShareUrl = "https://static.klipy.com/ii/c3a19a0b747a76e98651f2b9a3cca5ff/9c/db/xWbV5hZR.gif"
                }));
                if (Get(component, "importError") is not null || (await store.ReadAsync()).Stickers.Single().Bytes != original.Length)
                    throw new Exception("The supplied KLIPY GIF did not import successfully through the native component");
                Console.WriteLine("PASS supplied 13,700,196-byte GIF imports through native component after failed retries");
            }
            Set(component, "nativeDownloadClient", null);
            if (!log.Report().Contains("KLIPY GIF import")) throw new Exception("GIF errors were not logged");
            return 3;
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    private static object? Get(object target, string name) => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target);
    private static void Set(object target, string name, object? value) => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(target, value);
    private static Task Invoke(object target, string name, params object[] args)
        => (Task)target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(target, args)!;

    private sealed class NativeFormFactor : IFormFactor
    {
        public string GetFormFactor() => "Phone";
        public string GetPlatform() => "Android";
    }
    private sealed class FakeJs : IJSRuntime, IJSObjectReference
    {
        public ValueTask<T> InvokeAsync<T>(string identifier, object?[]? args) => InvokeAsync<T>(identifier, default, args);
        public ValueTask<T> InvokeAsync<T>(string identifier, CancellationToken token, object?[]? args)
            => ValueTask.FromResult(identifier == "import" ? (T)(object)this : identifier == "hasApiKey" ? (T)(object)true : default!);
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
    private sealed class ActualGifHandler(byte[] bytes) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) });
    }
    private sealed class ImportHandler(string mode) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            if (mode == "unexpected") throw new Exception("Unexpected native download failure");
            var content = new ByteArrayContent(new byte[12]);
            if (mode == "oversized") content.Headers.ContentLength = LibraryStore.MaxGifImportBytes + 1;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        }
    }
#pragma warning disable BL0006 // Exercise the real component's event boundary and render lifecycle.
    private sealed class ImportRenderer(IServiceProvider services) : Renderer(services, services.GetRequiredService<ILoggerFactory>())
    {
        public List<Exception> Errors { get; } = [];
        public override Dispatcher Dispatcher { get; } = Dispatcher.CreateDefault();
        protected override void HandleException(Exception exception) => Errors.Add(exception);
        protected override Task UpdateDisplayAsync(in RenderBatch batch) => Task.CompletedTask;
        public Task<GifSearch> MountAsync() => Dispatcher.InvokeAsync(async () =>
        {
            var component = (GifSearch)InstantiateComponent(typeof(GifSearch));
            await RenderRootComponentAsync(AssignRootComponentId(component), ParameterView.Empty);
            return component;
        });
    }
#pragma warning restore BL0006
}
