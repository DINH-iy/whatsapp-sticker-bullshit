namespace StickerApp.Core;

// Copy granted URIs while the platform permission is valid. The staged archive
// survives process restarts; importing always goes through PackArchive validation.
public static class PackFileInbox
{
    public static event Action? Changed;
    public static string? Folder { get; set; }
    public static string? Error { get; private set; }
    public static string? Peek() => Folder is { } folder && Directory.Exists(folder)
        ? Directory.EnumerateFiles(folder, "*.zip").Order(StringComparer.Ordinal).FirstOrDefault() : null;
    public static async Task ReceiveAsync(Stream source)
    {
        var folder = Folder ?? throw new InvalidOperationException("Pack import is not ready.");
        Directory.CreateDirectory(folder);
        if (Directory.EnumerateFiles(folder, "*.zip").Take(10).Count() >= 10)
            throw new IOException("Import or discard pending packs before opening more.");
        var path = Path.Combine(folder,  $"{DateTime.UtcNow.Ticks:D19}-{Guid.NewGuid():N}");
        try
        {
            await using (var output = File.Create(path + ".tmp"))
            {
                var buffer = new byte[81920]; long total = 0; int read;
                while ((read = await source.ReadAsync(buffer)) > 0)
                {
                    total += read;
                    if (total > PackArchive.MaxArchiveBytes) throw new InvalidDataException("This pack file is too large.");
                    await output.WriteAsync(buffer.AsMemory(0, read));
                }
            }
            File.Move(path + ".tmp", path + ".zip");
            Error = null;
            Changed?.Invoke();
        }
        finally { File.Delete(path + ".tmp"); }
    }
    public static void ClearError() { Error = null; Changed?.Invoke(); }
    public static void ReportError(string message) { Error = message; Changed?.Invoke(); }
    public static void Remove(string path)
    {
        if (Folder is { } folder && Directory.Exists(folder) && Directory.EnumerateFiles(folder, "*.zip").Contains(path, StringComparer.Ordinal))
            File.Delete(path);
        Changed?.Invoke();
    }
}
