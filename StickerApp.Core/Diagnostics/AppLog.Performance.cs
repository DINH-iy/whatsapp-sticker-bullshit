using System.Diagnostics;

namespace StickerApp.Core;

public sealed partial class AppLog
{
    public sealed record PerformanceEntry(DateTimeOffset Timestamp, string Operation, double Milliseconds, string Details);
    private readonly Queue<PerformanceEntry> performanceEntries = new();
    private bool performanceEnabled = File.Exists(Path.Combine(directory, "performance-enabled"));
    public bool PerformanceEnabled
    {
        get => performanceEnabled;
        set
        {
            performanceEnabled = value;
            try {
                Directory.CreateDirectory(directory);
                var path = Path.Combine(directory, "performance-enabled");
                if (value) File.WriteAllText(path, "true"); else File.Delete(path);
            } catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) {
                StorageError = "Could not save the performance recording preference.";
            }
        }
    }
    public IReadOnlyList<PerformanceEntry> PerformanceEntries { get { lock (gate) return performanceEntries.Reverse().ToArray(); } }
    public void ClearPerformance() { lock (gate) performanceEntries.Clear(); }
    public void RecordPerformance(string operation, double milliseconds, string? details = null)
    {
        if (!performanceEnabled || !double.IsFinite(milliseconds) || milliseconds < 0 || milliseconds > 86_400_000) return;
        operation = Clean(operation.Length > 120 ? operation[..120] : operation);
        details = Clean(details is { Length: > 240 } ? details[..240] : details ?? "");
        lock (gate) {
            performanceEntries.Enqueue(new(DateTimeOffset.UtcNow, operation, milliseconds, details));
            while (performanceEntries.Count > 100) performanceEntries.Dequeue();
        }
        Info("Performance", $"{operation}: {milliseconds:F1} ms. {details}");
    }
    public IDisposable Measure(string operation, string? details = null) => performanceEnabled
        ? new Measurement(this, operation, details) : NoMeasurement.Instance;
    private sealed class Measurement(AppLog owner, string operation, string? details) : IDisposable
    {
        private readonly long started = Stopwatch.GetTimestamp();
        private bool disposed;
        public void Dispose() { if (disposed) return; disposed = true; owner.RecordPerformance(operation, Stopwatch.GetElapsedTime(started).TotalMilliseconds, details); }
    }
    private sealed class NoMeasurement : IDisposable { public static readonly NoMeasurement Instance = new(); public void Dispose() { } }
}
