using System.Reflection;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.RenderTree;
using Microsoft.JSInterop;
using StickerApp.Core;
using StickerApp.Shared.Pages.Library;
using StickerApp.Shared.Services;

internal static class LibraryNavigationChecks
{
    public static async Task<int> RunAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "stik-library-navigation-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new LibraryStore(root);
            var pack = await store.CreatePackAsync("GIF destination");
            var png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVQIHWP4z8DwHwAFgAI/ScLttAAAAABJRU5ErkJggg==");
            await store.ImportAsync("In pack", png, pack);
            await store.ImportAsync("Outside pack", png);
            var log = new AppLog(Path.Combine(root, "logs"));
            await using var preparation = new StickerPreparationService(store, new NoJs(), log);
            await using var home = new Home();
            Set(home, "Store", store); Set(home, "Preparation", preparation); Set(home, "Log", log);
            Set(home, "L", new AppLanguage()); Set(home, "BulkImporter", new NoFolderPicker());
            Set(home, "Exporter", new BrowserWhatsAppExporter()); Set(home, "PackId", pack);
            await Invoke(home, "LoadLibrary");
            if (((Dictionary<Guid, string>)Get(home, "previews")!).Count != 0) throw new Exception("Navigation must not read original media eagerly");
            var gate = (SemaphoreSlim)Get(store, "gate")!;
            await gate.WaitAsync();
            Set(home, "PackId", null);
            var navigation = Invoke(home, "LoadLibrary");
            try
            {
                // Blazor renders again at the first incomplete await of parameter
                // loading. The visible IDs must still match the published previews.
                using var builder = new RenderTreeBuilder();
                typeof(Home).GetMethod("BuildRenderTree", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(home, [builder]);
                RenderChildren(builder);
            }
            finally { gate.Release(); await navigation; }
            if (Get(home, "activePack") is not null || ((Dictionary<Guid, string>)Get(home, "previews")!).Count != 0 || ((StickerLibrary)Get(home, "library")!).Stickers.Count != 2)
                throw new Exception("Pack-to-library navigation did not publish library metadata without eager media");
            Console.WriteLine("PASS pack-to-library navigation renders safely while previews are blocked, then publishes complete metadata without eager preview reads");
            return 1;
        }
        finally { Directory.Delete(root, true); }
    }
#pragma warning disable BL0006 // Render nested fragments at the suspended navigation boundary.
    private static void RenderChildren(RenderTreeBuilder builder)
    {
        var frames = builder.GetFrames();
        for (var i = 0; i < frames.Count; i++)
            if (frames.Array[i].FrameType == RenderTreeFrameType.Attribute && frames.Array[i].AttributeValue is RenderFragment fragment)
            {
                using var child = new RenderTreeBuilder();
                fragment(child); RenderChildren(child);
            }
    }
#pragma warning restore BL0006
    private static object? Get(object target, string name) => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target);
    private static void Set(object target, string name, object? value)
    {
        if (target.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) is { } property) property.SetValue(target, value);
        else target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(target, value);
    }
    private static Task Invoke(object target, string name) => (Task)target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(target, null)!;
    private sealed class NoFolderPicker : IBulkImageImporter
    {
        public bool IsAvailable => false;
        public string ActionLabel => "Import folder";
        public Task<IReadOnlyList<BulkImageFile>> PickFolderAsync() => Task.FromResult<IReadOnlyList<BulkImageFile>>([]);
    }
    private sealed class NoJs : IJSRuntime
    {
        public ValueTask<T> InvokeAsync<T>(string name, object?[]? args) => ValueTask.FromResult(default(T)!);
        public ValueTask<T> InvokeAsync<T>(string name, CancellationToken token, object?[]? args) => InvokeAsync<T>(name, args);
    }
}
