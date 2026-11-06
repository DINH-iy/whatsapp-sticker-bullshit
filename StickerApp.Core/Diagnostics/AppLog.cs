using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace StickerApp.Core;

/// <summary>Bounded, local-only diagnostics. A logging failure must never break an app action.</summary>
public sealed class AppLog(string directory)
{
    private readonly object gate = new();
    private const int MaxFileBytes = 256 * 1024;
    private const int Archives = 3;
    private readonly string session = Guid.NewGuid().ToString("N")[..12];
    public string? StorageError { get; private set; }
    private string FileName(int index) => Path.Combine(directory, index == 0 ? "stick-it.log" : $"stick-it.{index}.log");

    public string Error(string operation, Exception exception) => Write("Error", operation, exception.Message, exception);
    public string Info(string operation, string message) => Write("Information", operation, message);

    public string Write(string level, string operation, string message, Exception? exception = null)
    {
        var id = Guid.NewGuid().ToString("N")[..12];
        try
        {
            var entry = JsonSerializer.Serialize(new {
                timestamp = DateTimeOffset.UtcNow, id, session, level,
                operation = Clean(operation), message = Clean(message),
                exception = exception is null ? null : Clean(exception.ToString())
            }) + Environment.NewLine;
            lock (gate)
            {
                Directory.CreateDirectory(directory);
                if (File.Exists(FileName(0)) && new FileInfo(FileName(0)).Length + Encoding.UTF8.GetByteCount(entry) > MaxFileBytes)
                {
                    File.Delete(FileName(Archives));
                    for (var i = Archives - 1; i >= 0; i--)
                        if (File.Exists(FileName(i))) File.Move(FileName(i), FileName(i + 1), overwrite: true);
                }
                File.AppendAllText(FileName(0), entry, Encoding.UTF8);
                StorageError = null;
            }
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            StorageError = "The device could not write the diagnostic log. Check available storage and permissions.";
        }
        return id;
    }

    public string Read()
    {
        lock (gate)
        {
            try
            {
                var text = new StringBuilder();
                for (var i = Archives; i >= 0; i--)
                    if (File.Exists(FileName(i))) text.Append(File.ReadAllText(FileName(i)));
                return text.ToString();
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            { return "The diagnostic log could not be read. Check available storage and permissions."; }
        }
    }

    public string Report() => $"Stik-er-in diagnostic log\nExported: {DateTimeOffset.UtcNow:O}\n" +
        "Entries use UTC timestamps. Logs stay on this device until you export them.\n" +
        "URLs, embedded image data, and common credential fields are redacted. Review before sharing.\n\n" + Read();

    private static string Clean(string text)
    {
        // Bound each message before scanning it, including unexpectedly large JS errors.
        if (text.Length > 24000) text = text[..24000] + " [truncated]";
        text = Regex.Replace(text, @"data:[^\s,]*,[A-Za-z0-9+/=_%.-]+", "[image/data omitted]", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"https?://[^\s<>""']+", "[URL omitted]", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"\bBearer\s+\S+", "Bearer [redacted]", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"(?i)(\b(?:api[_-]?key|access[_-]?token|refresh[_-]?token|token|authorization|password|client[_-]?secret)\b[""']?\s*[:=]\s*[""']?)[^\s,""';}]+", "$1[redacted]");
        text = Regex.Replace(text, @"\b[A-Za-z0-9+/=_-]{100,}\b", "[long value omitted]");
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrEmpty(home)) text = text.Replace(home, "[home]", StringComparison.Ordinal);
        return text;
    }
}
