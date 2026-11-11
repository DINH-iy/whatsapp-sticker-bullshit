using System.Text.Json;
namespace StickerApp.Core;
public sealed partial class LibraryStore
{
    public int GifImportLimitBytes { get; private set; } = MaxGifImportBytes;
    private string MediaSettingsFile => Path.Combine(DefaultRoot, "media-settings.json");
    public static string GifLimitError(int bytes, bool decimalUnits = false)
    {
        if (!decimalUnits && bytes == MaxGifImportBytes) return GifSizeError;
        var amount = (bytes / (decimalUnits ? 1_000_000m : 1_048_576m)).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
        return $"This GIF exceeds the {amount} {(decimalUnits ? "MB" : "MiB")} import limit. Change GIF import limit in Settings → Imports, quality & editing or choose a smaller GIF.";
    }
    private sealed record MediaSettings(int GifImportLimitBytes = MaxGifImportBytes);
    private void LoadMediaSettings()
    {
        if (File.Exists(MediaSettingsFile))
            GifImportLimitBytes = Math.Clamp(JsonSerializer.Deserialize<MediaSettings>(File.ReadAllText(MediaSettingsFile))?.GifImportLimitBytes ?? MaxGifImportBytes, 5 * 1024 * 1024, 100 * 1024 * 1024);
    }
    public Task SetGifImportLimitAsync(int mebibytes)
    {
        if (mebibytes is < 5 or > 100) throw new ArgumentOutOfRangeException(nameof(mebibytes));
        return SetGifImportLimitBytesAsync(mebibytes * 1024 * 1024);
    }
    public async Task SetGifImportLimitBytesAsync(int bytes)
    {
        if (bytes is < 5 * 1024 * 1024 or > 100 * 1024 * 1024) throw new ArgumentOutOfRangeException(nameof(bytes));
        await gate.WaitAsync();
        try
        {
            await File.WriteAllTextAsync(MediaSettingsFile + ".tmp", JsonSerializer.Serialize(new MediaSettings(bytes), Json));
            File.Move(MediaSettingsFile + ".tmp", MediaSettingsFile, true);
            GifImportLimitBytes = bytes;
        }
        finally { gate.Release(); }
        Changed?.Invoke();
    }
}
