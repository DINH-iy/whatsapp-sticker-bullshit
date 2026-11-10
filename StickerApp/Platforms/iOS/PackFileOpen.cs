using Foundation;
using StickerApp.Core;

namespace StickerApp;

internal static class PackFileOpen
{
    private static readonly HashSet<string> receiving = [];
    public static bool Receive(NSUrl url)
    {
        if (!url.IsFileUrl || url.Path is not { } path ||
            Path.GetExtension(path).ToLowerInvariant() is not (".wastickers" or ".wasticker" or ".wassticker" or ".zip")) return false;
        if (receiving.Add(path)) _ = CopyAsync(url, path);
        return true;
    }
    private static async Task CopyAsync(NSUrl url, string path)
    {
        var scoped = url.StartAccessingSecurityScopedResource();
        try
        {
            await using var stream = File.OpenRead(path);
            await PackFileInbox.ReceiveAsync(stream);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException)
        { PackFileInbox.ReportError(ex.Message); }
        finally { if (scoped) url.StopAccessingSecurityScopedResource(); receiving.Remove(path); }
    }
}
