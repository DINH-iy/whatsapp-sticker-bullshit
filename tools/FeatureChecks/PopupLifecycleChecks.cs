using System.Reflection;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using StickerApp.Core;
using StickerApp.Shared.Components.Infrastructure;
using StickerApp.Shared.Services;

internal static class PopupLifecycleChecks
{
    public static async Task<int> RunAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "stik-popup-lifecycle-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new LibraryStore(root);
            var log = new AppLog(Path.Combine(root, "logs"));
            var cases = new (string Name, int Stages, string? Flag)[]
            {
                ("Common.AppGuideDialog", 4, null), ("Common.AppDisclosure", 2, "Popup"),
                ("Common.ActionMenu", 2, "open"), ("Common.ColorPicker", 2, "opening"),
                ("Common.ProfilePicture", 2, "opening"), ("Common.BrowseOptionsPopup", 2, null),
                ("Library.VideoDevicePicker", 2, null), ("Library.ImportOriginalChoices", 2, null),
                ("Gifs.GifSearchDialog", 2, null), ("Packs.DeletePackDialog", 2, null),
                ("Packs.NewPackDialog", 2, null), ("Packs.AddToPackDialog", 2, null),
                ("Packs.SharePackDialog", 3, null), ("Editor.VideoImportDialog", 4, null),
                ("Editor.StickerInfoDialog", 2, null), ("Editor.ConfirmLayeringDialog", 2, null),
                ("Editor.ConfirmToolCancel", 2, null), ("Editor.Layout.EditorPopup", 2, "opening"),
                ("Editor.ImageCustomizer", 4, null), ("Editor.Layers.LayerEditorDialog", 4, null),
                ("Settings.ProfileEditor", 2, "Onboarding"), ("Settings.FolderPicker", 2, null),
                ("Settings.FactoryResetSettings", 2, "openDialog"),
                ("Settings.FactoryResetProgressView", 2, null), ("Settings.ThemeColorPicker", 2, null),
                ("Editor.AnimationTimeline", 2, null), ("Infrastructure.ProcessingMenu", 2, null),
                ("Infrastructure.DiagnosticsHost", 4, null), ("Infrastructure.NavigationHost", 1, null),
                ("Settings.LayerWarningSettings", 2, null), ("Settings.EditorLayoutSettings", 2, null),
                ("Settings.GifSearchSettings", 2, null), ("Settings.PackSharingSettings", 2, null)
            };
            var passed = 0;
            foreach (var (name, stages, flag) in cases)
            {
                var type = typeof(InteropComponentBase).Assembly.GetType("StickerApp.Shared.Components." + name)!;
                for (var stage = 1; stage <= stages; stage++)
                {
                    var js = new DelayedJs(stage);
                    await using var preparation = new StickerPreparationService(store, js, log);
                    var component = (InteropComponentBase)Activator.CreateInstance(type)!;
                    foreach (var property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                        .Where(property => property.IsDefined(typeof(InjectAttribute))))
                    {
                        object? value = property.PropertyType == typeof(IJSRuntime) ? js
                            : property.PropertyType == typeof(AppLanguage) ? new AppLanguage()
                            : property.PropertyType == typeof(LibraryStore) ? store
                            : property.PropertyType == typeof(AppLog) ? log
                            : property.PropertyType == typeof(StickerPreparationService) ? preparation : null;
                        if (value is not null) property.SetValue(component, value);
                    }
                    if (flag is not null)
                    {
                        if (type.GetProperty(flag) is { } property) property.SetValue(component, true);
                        else type.GetField(flag, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(component, true);
                    }
                    var initialize = (Task)typeof(InteropComponentBase).GetMethod("OnAfterRenderAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
                        .Invoke(component, [true])!;
                    await js.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
                    await component.DisposeAsync();
                    if (js.References.Any(reference => reference.Disposals != 0)) throw new Exception(name + " released an in-use reference");
                    js.Release.SetResult();
                    await initialize.WaitAsync(TimeSpan.FromSeconds(5));
                    await component.DisposeAsync();
                    if (js.References.Any(reference => reference.Disposals != 1)) throw new Exception(name + " leaked or double-disposed a late reference");
                    if (js.InitializationCalls != stage) throw new Exception(name + " continued initialization after removal");
                    passed++;
                }
                Console.WriteLine($"PASS {name}: removal during each of {stages} interop stages releases references safely");
            }
            foreach (var failure in new Exception[] { new JSException("Detached element"), new ObjectDisposedException("Reference"), new OperationCanceledException() })
            {
                var probe = new CompletionProbe();
                var pending = probe.CompleteAsync(failure);
                await probe.DisposeAsync();
                if (probe.Cleanups != 0) throw new Exception("An event's references were disposed before it completed");
                probe.Release.SetResult();
                await pending;
                await probe.DisposeAsync();
                if (probe.Cleanups != 1) throw new Exception("A removed event did not release its references exactly once");
                passed++;
            }
            var active = new CompletionProbe();
            active.Release.SetResult();
            try { await active.CompleteAsync(new JSException("Real failure")); throw new Exception("Active component errors were hidden"); }
            catch (JSException) { passed++; }
            await active.DisposeAsync();
            Console.WriteLine("PASS late callback failures after removal are contained; active component errors remain visible");
            return passed;
        }
        finally { Directory.Delete(root, true); }
    }

    private sealed class CompletionProbe : InteropComponentBase
    {
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Cleanups { get; private set; }
        public Task CompleteAsync(Exception failure) => RunInteropAsync(async () => { await Release.Task; throw failure; });
        protected override ValueTask DisposeInteropAsync() { Cleanups++; return ValueTask.CompletedTask; }
    }

    private sealed class DelayedJs(int pauseAt) : IJSRuntime
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public List<Reference> References { get; } = [];
        public int InitializationCalls { get; private set; }
        public ValueTask<T> InvokeAsync<T>(string name, object?[]? args) => InvokeAsync<T>(name, default, args);
        public async ValueTask<T> InvokeAsync<T>(string name, CancellationToken token, object?[]? args)
        {
            if (name is "dispose" or "release") return default!;
            if (++InitializationCalls == pauseAt) { Entered.SetResult(); await Release.Task; }
            if (typeof(T) == typeof(IJSObjectReference))
            {
                var reference = new Reference(this); References.Add(reference); return (T)(object)reference;
            }
            return typeof(T).IsClass && typeof(T) != typeof(string) ? Activator.CreateInstance<T>() : default!;
        }
    }
    private sealed class Reference(DelayedJs js) : IJSObjectReference
    {
        public int Disposals { get; private set; }
        public ValueTask<T> InvokeAsync<T>(string name, object?[]? args) => InvokeAsync<T>(name, default, args);
        public ValueTask<T> InvokeAsync<T>(string name, CancellationToken token, object?[]? args)
        {
            ObjectDisposedException.ThrowIf(Disposals != 0, this);
            return js.InvokeAsync<T>(name, token, args);
        }
        public ValueTask DisposeAsync() { Disposals++; return ValueTask.CompletedTask; }
    }
}
