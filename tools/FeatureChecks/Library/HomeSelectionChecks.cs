using System.Reflection;
using Microsoft.JSInterop;
using StickerApp.Core;
using StickerApp.Shared.Pages.Library;
using StickerApp.Shared.Services;

internal static class HomeSelectionChecks
{
    public static async Task<int> RunAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "stik-home-selection-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new LibraryStore(root);
            var pack = await store.CreatePackAsync("Selected pack");
            var other = await store.CreatePackAsync("Other pack");
            var png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVQIHWP4z8DwHwAFgAI/ScLttAAAAABJRU5ErkJggg==");
            var first = await store.ImportAsync("First", png, pack);
            var second = await store.ImportAsync("Second", png, pack);
            var outside = await store.ImportAsync("Outside", png, other);
            var snapshot = await store.ReadBrowseSnapshotAsync();
            var js = new RejectJs();
            var log = new AppLog(Path.Combine(root, "logs"));
            await using var preparation = new StickerPreparationService(store, js, log);
            await using var home = new Home();
            Set(home, "Store", store); Set(home, "Preparation", preparation); Set(home, "Log", log);
            Set(home, "library", snapshot.Library); Set(home, "browse", snapshot);
            var selected = (HashSet<Guid>)Get(home, "selected")!;
            Invoke(home, "ToggleSelectionMode");
            Invoke(home, "SelectAllVisible");
            Require(selected.SetEquals([first, second, outside]), "Select all did not include the library");
            await (Task)Invoke(home, "ActivateImage", snapshot.Library.Stickers.Single(image => image.Id == first))!;
            Require(!selected.Contains(first) && Get(home, "editing") is null && js.Calls == 0, "Selection opened an editor or started media work");
            selected.Clear(); Set(home, "search", "Outside"); Invoke(home, "SelectAllVisible");
            Require(selected.SetEquals([outside]), "Select all ignored the current search");
            selected.Clear(); Set(home, "search", ""); Set(home, "activePack", pack); Invoke(home, "SelectAllVisible");
            Require(selected.SetEquals([first, second]), "Select all escaped the current pack");
            Invoke(home, "RequestBulkRemoval");
            selected.Clear(); selected.Add(outside);
            await (Task)Invoke(home, "ConfirmRemoval")!;
            var after = await store.ReadAsync();
            Require(after.Stickers.Single().Id == outside && after.Packs.Single(item => item.Id == other).StickerIds.SequenceEqual([outside]) &&
                after.Packs.Single(item => item.Id == pack).StickerIds.Count == 0, "Bulk confirmation deleted outside its snapshot or left pack references behind");
            Require(Get(home, "pendingBulkRemoval") is null, "Bulk deletion left a stale confirmation");
            Invoke(home, "ToggleSelectionMode");
            Require(selected.Count == 0 && Get(home, "selectionMode") is false, "Leaving select mode retained the selection");
            Console.WriteLine("PASS library/pack/search selection never opens media; bulk deletion uses the confirmed snapshot and keeps unrelated stickers/packs");
            return 6;
        }
        finally { Directory.Delete(root, true); }
    }
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static object? Get(object target, string name) => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target);
    private static void Set(object target, string name, object value)
    {
        if (target.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.NonPublic) is { } property) property.SetValue(target, value);
        else target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(target, value);
    }
    private static object? Invoke(object target, string name, params object[] args) => target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(target, args);
    private sealed class RejectJs : IJSRuntime
    {
        public int Calls;
        public ValueTask<T> InvokeAsync<T>(string name, object?[]? args) => InvokeAsync<T>(name, default, args);
        public ValueTask<T> InvokeAsync<T>(string name, CancellationToken token, object?[]? args)
        { Calls++; throw new Exception("Selection must not call JavaScript: " + name); }
    }
}
