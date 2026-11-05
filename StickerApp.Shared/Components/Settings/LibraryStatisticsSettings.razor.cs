using StickerApp.Core;
using Microsoft.AspNetCore.Components;

namespace StickerApp.Shared.Components.Settings;

public partial class LibraryStatisticsSettings
{
    private LibraryStatistics? statistics;
    private LibraryScan? scan;
    private Guid[] keepers = [];
    private bool busy;
    private bool confirmDuplicates;
    private bool confirmUnused;
    private bool confirmOriginals;
    private bool keepStills;
    private bool keepAnimations;
    private bool limitEnabled;
    private decimal limitAmount = 500;
    private bool decimalUnits;
    private bool donutChart;
    private bool askOnImport;
    private string LimitUnit => decimalUnits ? "MB" : "MiB";
    private decimal LimitMultiplier => decimalUnits ? 1_000_000m : 1_048_576m;
    private decimal MinimumLimit => 1_048_576m / LimitMultiplier;
    private decimal MaximumLimit => 1_099_511_627_776m / LimitMultiplier;
    private static string InputNumber(decimal value) => value.ToString(System.Globalization.CultureInfo.InvariantCulture);
    private readonly Dictionary<string, string> duplicatePreviews = [];
    private string? error;
    private string? notice;
    private string progress = "Loading statistics…";
    private int DuplicateCount => scan?.Duplicates.Sum(group => group.Images.Count - 1) ?? 0;

    protected override Task OnInitializedAsync() => Refresh();

    private async Task Refresh() => await Run("Measuring library storage…", async () =>
    {
        ClearReview();
        statistics = await Store.GetStatisticsAsync();
        keepStills = Store.KeepOriginalStillImages;
        keepAnimations = Store.KeepOriginalAnimations;
        limitEnabled = Store.StorageLimitBytes.HasValue;
        decimalUnits = Store.DecimalStorageUnits;
        donutChart = Store.DonutStorageChart;
        askOnImport = Store.AskForOriginalOnImport;
        limitAmount = (Store.StorageLimitBytes ?? 500L * 1024 * 1024) / LimitMultiplier;
    });

    private async Task Scan() => await Run("Comparing image and source files…", async () =>
    {
        ClearReview();
        scan = await Store.ScanLibraryAsync();
        statistics = scan.Statistics;
        keepers = scan.Duplicates.Select(group => group.Images[0].Id).ToArray();
        var library = await Store.ReadAsync();
        foreach (var group in scan.Duplicates)
        {
            var image = library.Stickers.FirstOrDefault(image => image.Id == group.Images[0].Id);
            if (image is not null) duplicatePreviews[group.Fingerprint] = await Store.PreviewAsync(image);
        }
    });

    private async Task ChangeStillRetention(ChangeEventArgs args) => await ChangeRetention(args.Value is true, keepAnimations);
    private async Task ChangeAnimationRetention(ChangeEventArgs args) => await ChangeRetention(keepStills, args.Value is true);
    private async Task ChangeImportPrompts(ChangeEventArgs args) => await Run("Saving import preference…", async () =>
    {
        await Store.SetAskForOriginalOnImportAsync(!(args.Value is true));
        askOnImport = Store.AskForOriginalOnImport;
    });

    private async Task ChangeRetention(bool stills, bool animations) => await Run("Saving source preferences…", async () =>
    {
        await Store.SetOriginalRetentionAsync(stills, animations);
        keepStills = Store.KeepOriginalStillImages;
        keepAnimations = Store.KeepOriginalAnimations;
        ClearReview();
        notice = "Source preferences saved. Existing originals stay until you edit their images or explicitly remove originals below.";
    });

    private async Task SaveStorageLimit() => await Run("Saving library storage limit…", async () =>
    {
        if (limitEnabled && (limitAmount < MinimumLimit || limitAmount > MaximumLimit))
            throw new InvalidOperationException($"Choose a limit from {MinimumLimit} to {MaximumLimit} {LimitUnit}.");
        await Store.SetStorageLimitAsync(limitEnabled ? decimal.ToInt64(decimal.Round(limitAmount * LimitMultiplier)) : null);
        ClearReview();
        statistics = await Store.GetStatisticsAsync();
        notice = "Library storage limit saved. Existing files were kept.";
    });

    private async Task ChangeUnits(ChangeEventArgs args) => await Run("Saving display units…", async () =>
    {
        var draftBytes = limitAmount * LimitMultiplier;
        var selected = args.Value?.ToString() == "decimal";
        await Store.SetDecimalStorageUnitsAsync(selected);
        decimalUnits = Store.DecimalStorageUnits;
        limitAmount = draftBytes / LimitMultiplier;
    });

    private async Task ChangeChart(bool useDonut) => await Run("Saving chart style…", async () =>
    {
        await Store.SetDonutStorageChartAsync(useDonut);
        donutChart = Store.DonutStorageChart;
    });

    private async Task RemoveOriginals()
    {
        if (scan is null || !confirmOriginals) return;
        await Run("Removing reviewed originals…", async () =>
        {
            var freed = await Store.RemoveOriginalsAsync(scan.Statistics.Root, scan.OriginalFiles);
            ClearReview();
            statistics = await Store.GetStatisticsAsync();
            notice = $"Freed {Size(freed)} from originals. Prepared stickers and editable layers were kept. Files still needed for preparation were kept.";
        });
    }

    private async Task RemoveDuplicates()
    {
        if (scan is null || !confirmDuplicates) return;
        await Run("Removing reviewed copies…", async () =>
        {
            var selections = scan.Duplicates.Select((group, index) => new DuplicateSelection(group.Fingerprint,
                keepers[index], group.Images.Where(image => image.Id != keepers[index]).Select(image => image.Id).ToList())).ToList();
            var count = await Store.RemoveDuplicatesAsync(selections);
            ClearReview();
            statistics = await Store.GetStatisticsAsync();
            notice = $"Removed {count} duplicate copies. Pack memberships, favorites, and tags were merged. Refresh or scan again to see the latest library.";
        });
    }

    private async Task RemoveUnused()
    {
        if (scan is null || !confirmUnused) return;
        await Run("Removing reviewed unused files…", async () =>
        {
            var freed = await Store.RemoveUnusedFilesAsync(scan.Statistics.Root, scan.UnusedFiles);
            ClearReview();
            statistics = await Store.GetStatisticsAsync();
            notice = $"Freed {Size(freed)}. Files still in use were kept. Any files that could not be deleted remain in the storage total.";
        });
    }

    private void ClearReview()
    {
        scan = null;
        keepers = [];
        duplicatePreviews.Clear();
        confirmDuplicates = confirmUnused = confirmOriginals = false;
    }

    private async Task Run(string message, Func<Task> action)
    {
        if (busy) return;
        busy = true;
        progress = message;
        error = notice = null;
        try { await action(); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
            InvalidOperationException or System.Text.Json.JsonException or System.Security.Cryptography.CryptographicException)
        {
            var reference = Log.Error("Library statistics and cleanup", exception);
            ClearReview();
            error = $"Could not complete this operation. {exception.Message} Reference: {reference}. Refresh or scan again to retry.";
        }
        finally { busy = false; }
    }

    private string Size(long bytes) => StorageDisplay.Format(bytes, decimalUnits);

    private static string PackWidth(PackStorageStatistics pack, IReadOnlyList<PackStorageStatistics> packs)
    {
        var maximum = packs.Max(item => item.StickerBytes + item.SourceBytes);
        return maximum == 0 ? "0%" : ((pack.StickerBytes + pack.SourceBytes) * 100d / maximum)
            .ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + "%";
    }

    private string StorageWidth(long bytes)
    {
        return Invariant(StoragePercent(bytes)) + "%";
    }

    private double StoragePercent(long bytes)
    {
        var used = statistics is null ? 0 : statistics.MediaBytes + statistics.IndexBytes;
        var scale = Math.Max(used, Store.StorageLimitBytes ?? 0);
        return scale == 0 ? 0 : bytes * 100d / scale;
    }

    private static string Invariant(double number) => number.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture);

    private IEnumerable<(LibraryStorageSegment Segment, string Dash, string Offset)> DonutSlices()
    {
        var offset = 0d;
        foreach (var segment in statistics?.StorageSegments ?? [])
        {
            var percent = StoragePercent(segment.Bytes);
            yield return (segment, $"{Invariant(percent)} {Invariant(100 - percent)}", Invariant(-offset));
            offset += percent;
        }
    }
}
