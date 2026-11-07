using System.Text.Json;

namespace StickerApp.Core;

public sealed partial class LibraryStore
{
    public Task SetStickerNsfwAsync(Guid id, bool value) => MutateAsync(library =>
    {
        var sticker = library.Stickers.FirstOrDefault(item => item.Id == id)
            ?? throw new InvalidOperationException("This sticker no longer exists.");
        sticker.Nsfw = value;
        foreach (var pack in library.Packs.Where(pack => pack.StickerIds.Contains(id))) Touch(pack);
    });

    public Task SetPackNsfwAsync(Guid id, bool value) => MutateAsync(library =>
    {
        var pack = library.Packs.FirstOrDefault(item => item.Id == id)
            ?? throw new InvalidOperationException("This pack no longer exists.");
        pack.Nsfw = value;
        Touch(pack);
    });

    public async Task SetBlurNsfwPreviewsAsync(bool enabled)
    {
        await gate.WaitAsync();
        try
        {
            await File.WriteAllTextAsync(settingsFile + ".tmp", JsonSerializer.Serialize(
                new StorageSettings(Root, KeepOriginalStillImages, KeepOriginalAnimations, StorageLimitBytes,
                    DecimalStorageUnits, DonutStorageChart, AskForOriginalOnImport, enabled), Json));
            File.Move(settingsFile + ".tmp", settingsFile, true);
            BlurNsfwPreviews = enabled;
        }
        finally { gate.Release(); }
        Changed?.Invoke();
    }
}
