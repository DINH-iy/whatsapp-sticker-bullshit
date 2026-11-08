using System.Text.Json;

namespace StickerApp.Core;

public sealed record PackActivity(Guid Id, string Name, long Updates, DateTimeOffset? LastUpdatedUtc);

public sealed partial class LibraryStore
{
    // Count each successful index commit once per affected pack, even for a multi-file batch.
    // Old libraries have zero history; importing a pack creates a fresh local history.
    private static void TrackPackUpdates(StickerLibrary previous, StickerLibrary current)
    {
        var before = previous.Packs.ToDictionary(pack => pack.Id);
        foreach (var pack in current.Packs)
        {
            if (!before.TryGetValue(pack.Id, out var old)) continue;
            pack.UpdateCount = old.UpdateCount;
            pack.LastUpdatedUtc = old.LastUpdatedUtc;
            if (pack.Version == old.Version || PackContent(previous, old) == PackContent(current, pack)) continue;
            pack.UpdateCount = old.UpdateCount < long.MaxValue ? old.UpdateCount + 1 : long.MaxValue;
            pack.LastUpdatedUtc = DateTimeOffset.UtcNow;
        }
    }

    private static string PackContent(StickerLibrary library, StickerPack pack) => JsonSerializer.Serialize(new
    {
        pack.Name, pack.Publisher, pack.Nsfw, pack.Public, pack.Animated, pack.CoverImage,
        pack.StickerIds,
        Stickers = pack.StickerIds.Select(id => library.Stickers.FirstOrDefault(sticker => sticker.Id == id))
    });
}
