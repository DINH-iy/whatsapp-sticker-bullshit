using System.Text.Json;
namespace StickerApp.Core;
public sealed partial class LibraryStore
{
    public int GifImportLimitBytes { get; private set; } = MaxGifImportBytes;
    private string MediaSettingsFile => Path.Combine(DefaultRoot, "media-settings.json");
    public static string GifLimitError(int bytes) => bytes == MaxGifImportBytes ? GifSizeError : $"This GIF exceeds the {bytes / (1024 * 1024)} MiB import limit. Change GIF import limit in Settings → Editing or choose a smaller GIF.";
    private sealed record MediaSettings(int GifImportLimitBytes = MaxGifImportBytes);
    private void LoadMediaSettings()
    {
        if (File.Exists(MediaSettingsFile))
            GifImportLimitBytes = Math.Clamp(JsonSerializer.Deserialize<MediaSettings>(File.ReadAllText(MediaSettingsFile))?.GifImportLimitBytes ?? MaxGifImportBytes, 5 * 1024 * 1024, 100 * 1024 * 1024);
    }
    public async Task SetGifImportLimitAsync(int mebibytes)
    {
        if (mebibytes is < 5 or > 100) throw new ArgumentOutOfRangeException(nameof(mebibytes));
        await gate.WaitAsync();
        try
        {
            var bytes = mebibytes * 1024 * 1024;
            await File.WriteAllTextAsync(MediaSettingsFile + ".tmp", JsonSerializer.Serialize(new MediaSettings(bytes), Json));
            File.Move(MediaSettingsFile + ".tmp", MediaSettingsFile, true);
            GifImportLimitBytes = bytes;
        }
        finally { gate.Release(); }
        Changed?.Invoke();
    }
}
