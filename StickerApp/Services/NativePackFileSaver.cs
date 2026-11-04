using StickerApp.Shared.Services;

namespace StickerApp.Services;

public sealed class NativePackFileSaver : IPackFileSaver
{
    public string ActionLabel => "Share pack file";

    public async Task SaveAsync(string fileName, Stream content)
    {
        var sharingRoot = Path.Combine(FileSystem.CacheDirectory, "sharing-root");
        Directory.CreateDirectory(sharingRoot);

        // Retain recent files because another app may read them after the share sheet closes.
        foreach (var directory in Directory.EnumerateDirectories(sharingRoot))
        {
            if (Directory.GetLastWriteTimeUtc(directory) < DateTime.UtcNow.AddDays(-7))
            {
                try { Directory.Delete(directory, recursive: true); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }

        var destination = Path.Combine(sharingRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(destination);
        var file = Path.Combine(destination, fileName);
        await using (var output = File.Create(file))
            await content.CopyToAsync(output);

        await MainThread.InvokeOnMainThreadAsync(() => Share.Default.RequestAsync(new ShareFileRequest
        {
            Title = "Share sticker pack",
            File = new ShareFile(file, "application/zip")
        }));
    }
}
