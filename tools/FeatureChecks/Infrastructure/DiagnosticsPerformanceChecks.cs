using StickerApp.Core;
using StickerApp.Shared.Services;

internal static class DiagnosticsPerformanceChecks
{
    public static Task<int> RunAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "stik-performance-" + Guid.NewGuid().ToString("N"));
        try {
            var log = new AppLog(root);
            using (log.Measure("disabled")) { }
            Require(log.PerformanceEntries.Count == 0, "Performance recording must be opt-in");
            log.PerformanceEnabled = true;
            Require(new AppLog(root).PerformanceEnabled, "Performance preference did not persist");
            using (log.Measure("Editor: opening", "frames=300")) { }
            Require(log.PerformanceEntries.Single().Milliseconds >= 0, "Operation duration missing");
            log.RecordPerformance("invalid", double.NaN);
            Require(log.PerformanceEntries.Count == 1, "Invalid duration was accepted");
            for (var i = 0; i < 110; i++) log.RecordPerformance("Step " + i, i);
            Require(log.PerformanceEntries.Count == 100 && log.PerformanceEntries[0].Operation == "Step 109", "Performance list must be bounded and newest first");
            Require(log.Report().Contains("Performance") && log.Report().Contains("frames=300"), "Timings missing from exported diagnostic log");
            log.ClearPerformance(); Require(log.PerformanceEntries.Count == 0, "Clear timings did not clear list");
            log.PerformanceEnabled = false; Require(!new AppLog(root).PerformanceEnabled, "Disabling did not persist");
            var animation = new Sticker { Animated = true, Bytes = 11620 * 1024L, MimeType = "image/gif" };
            var size = PreparationProblem.Describe(animation, "This animation exceeds 500 KB: at least 540 KB.");
            Require(size.Summary.Contains("500 KB") && size.Recovery.Contains("shorten", StringComparison.OrdinalIgnoreCase) && size.EditLabel.Contains("Shorten"), "Size failure must offer an actual recovery action");
            var duration = PreparationProblem.Describe(animation, "This GIF is longer than 10 seconds.");
            Require(duration.Recovery.Contains("start and end handles"), "Duration failure must explain the trim controls");
            var timeout = PreparationProblem.Describe(animation, "Preparation took too long.");
            Require(timeout.Recovery.Contains("try again"), "Timeout must explain retry");
            Console.WriteLine("PASS opt-in/persisted/bounded performance recording, export and preparation problem recovery guidance");
            return Task.FromResult(11);
        } finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
    private static void Require(bool value, string message) { if (!value) throw new Exception(message); }
}
