using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace StickerApp.Shared.Components.Settings;

public partial class LibraryStatisticsSettings
{
    private static readonly string[] DefaultQuickStats = ["images", "packs", "used", "unused"];
    private List<string> quickStats = [.. DefaultQuickStats];
    private ElementReference statisticsElement;
    private IJSObjectReference? layoutModule;
    private IJSObjectReference? layoutController;
    private DotNetObjectReference<LibraryStatisticsSettings>? layoutReference;
    private bool customizingStats;
    private bool layoutReady;
    private bool layoutInitializing;
    private bool layoutSaving;
    private string? layoutNotice;
    private sealed record QuickStatPreferences(string[]? Selected);

    private IReadOnlyList<LibraryStatistic> AllStats => statistics is not { } stats ? [] :
    [
        new("images", "Images", "image", stats.Images.ToString("N0")),
        new("still", "Still images", "image", (stats.Images - stats.AnimatedImages).ToString("N0")),
        new("animations", "GIFs / animations", "animation", stats.AnimatedImages.ToString("N0")),
        new("packs", "Packs", "folder-plus", $"{stats.Packs} / 10"),
        new("used", "Total used", "storage", Size(stats.MediaBytes + stats.IndexBytes)),
        new("current", "Current images", "image", Size(stats.CurrentBytes)),
        new("originals", "Preserved originals", "archive", Size(stats.OriginalBytes)),
        new("fonts", "Custom fonts", "text", Size(stats.FontBytes), $"({stats.Fonts.Count} fonts)"),
        new("layers", "Editable layers", "layers", Size(stats.LayerBytes)),
        new("unused", "Unused files", "trash", Size(stats.UnusedBytes), $"({stats.UnusedFiles} files)"),
        new("index", "Library index", "file", Size(stats.IndexBytes)),
        new("other", "Other files", "file", Size(Math.Max(0, stats.MediaBytes - stats.CurrentBytes - stats.OriginalBytes - stats.LayerBytes - stats.FontBytes - stats.UnusedBytes)))
    ];
    private IReadOnlyList<LibraryStatistic> QuickStats => quickStats
        .Select(id => AllStats.FirstOrDefault(stat => stat.Id == id)).OfType<LibraryStatistic>().ToList();

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (statistics is null || layoutInitializing) return;
        layoutInitializing = true;
        try
        {
            layoutModule = await JS.InvokeAsync<IJSObjectReference>("import", "./_content/StickerApp.Shared/js/ui/quick-statistics.js");
            var preferences = await layoutModule.InvokeAsync<QuickStatPreferences?>("load");
            if (preferences?.Selected is { } selected)
            {
                var valid = AllStats.Select(stat => stat.Id).ToHashSet();
                quickStats = selected.Where(valid.Contains).Distinct().Take(valid.Count).ToList();
            }
            layoutReference = DotNetObjectReference.Create(this);
            layoutController = await layoutModule.InvokeAsync<IJSObjectReference>("connect", statisticsElement, layoutReference);
            layoutReady = true;
        }
        catch (JSDisconnectedException) { return; }
        catch (JSException exception)
        {
            Log.Error("Quick statistics layout", exception);
            layoutNotice = "Quick-stat customization could not be loaded. Reload to try again.";
        }
        StateHasChanged();
    }

    private async Task SaveQuickStats()
    {
        if (layoutModule is null) return;
        layoutSaving = true;
        try
        {
            var saved = await layoutModule.InvokeAsync<bool>("save", quickStats);
            layoutNotice = saved ? "Quick stats saved on this browser/device." : "Layout updated for this visit; device storage could not save it.";
        }
        catch (JSDisconnectedException) { }
        catch (JSException exception)
        {
            Log.Error("Save quick statistics layout", exception);
            layoutNotice = "Layout updated for this visit; device storage could not save it.";
        }
        finally { layoutSaving = false; }
    }

    private async Task ToggleQuickStat(string id)
    {
        if (layoutSaving || busy || !AllStats.Any(stat => stat.Id == id)) return;
        if (!quickStats.Remove(id)) quickStats.Add(id);
        await SaveQuickStats();
    }

    private async Task MoveQuickStat(string id, int direction)
    {
        if (layoutSaving || busy) return;
        var index = quickStats.IndexOf(id);
        var target = index + direction;
        if (index < 0 || target < 0 || target >= quickStats.Count) return;
        (quickStats[index], quickStats[target]) = (quickStats[target], quickStats[index]);
        await SaveQuickStats();
    }

    [JSInvokable]
    public Task DropQuickStat(string id, string? targetId, string zone, bool after) => InvokeAsync(async () =>
    {
        if (!customizingStats || layoutSaving || busy || !AllStats.Any(stat => stat.Id == id)) return;
        if (zone == "quick")
        {
            if (targetId == id) return;
            quickStats.Remove(id);
            var index = targetId is null ? -1 : quickStats.IndexOf(targetId);
            quickStats.Insert(index < 0 ? quickStats.Count : index + (after ? 1 : 0), id);
        }
        else if (zone == "library") quickStats.Remove(id);
        else return;
        await SaveQuickStats();
        StateHasChanged();
    });

    private async Task ResetQuickStats()
    {
        if (layoutSaving || busy) return;
        quickStats = [.. DefaultQuickStats];
        await SaveQuickStats();
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (layoutController is not null)
            {
                await layoutController.InvokeVoidAsync("dispose");
                await layoutController.DisposeAsync();
            }
            if (layoutModule is not null) await layoutModule.DisposeAsync();
        }
        catch (JSDisconnectedException) { }
        finally { layoutReference?.Dispose(); }
    }
}
