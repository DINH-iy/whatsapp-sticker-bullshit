using System.Text.Json;

namespace StickerApp.Core;

public sealed record ProfileIdentity(Guid Id, DateTimeOffset CreatedUtc);
public sealed record UserStatistics(int Stickers, int StillImages, int AnimatedImages, int LayeredStickers,
    int Favorites, int Packs, int PublicPacks, int PrivatePacks, int Fonts, long FontBytes, long StorageBytes, long PackUpdates);
public sealed record ProfileOverview(LocalProfile Profile, ProfileIdentity Identity, UserStatistics Statistics, IReadOnlyList<PackActivity> PackActivity);

public sealed partial class LibraryStore
{
    // Local identity only: no endpoint, credentials, telemetry or network call is involved.
    // Separate from editable profile fields so changing a name/photo cannot replace the identity.
    public async Task<ProfileOverview> ReadProfileOverviewAsync()
    {
        await gate.WaitAsync();
        try
        {
            var profile = File.Exists(ProfileFile)
                ? JsonSerializer.Deserialize<LocalProfile>(await File.ReadAllTextAsync(ProfileFile), Json) ?? new()
                : new();
            var identityPath = Path.Combine(DefaultRoot, "profile-identity.json");
            ProfileIdentity identity;
            if (File.Exists(identityPath))
                identity = JsonSerializer.Deserialize<ProfileIdentity>(await File.ReadAllTextAsync(identityPath), Json)
                    ?? throw new InvalidDataException("The local profile identity could not be read.");
            else
            {
                identity = new(Guid.NewGuid(), DateTimeOffset.UtcNow);
                await File.WriteAllTextAsync(identityPath + ".tmp", JsonSerializer.Serialize(identity, Json));
                File.Move(identityPath + ".tmp", identityPath, true);
            }
            if (identity.Id == Guid.Empty) throw new InvalidDataException("The local profile identity is invalid.");
            var library = await ReadInternalAsync();
            var storage = MeasureLibrary(library);
            var animated = library.Stickers.Count(sticker => sticker.Animated);
            var publicPacks = library.Packs.Count(pack => pack.Public);
            return new(profile, identity, new(library.Stickers.Count, library.Stickers.Count - animated, animated,
                library.Stickers.Count(sticker => sticker.CompositionFileName is not null),
                library.Stickers.Count(sticker => sticker.Favorite), library.Packs.Count, publicPacks,
                library.Packs.Count - publicPacks, library.Fonts.Count, storage.FontBytes, storage.MediaBytes + storage.IndexBytes, library.Packs.Sum(pack => pack.UpdateCount)),
                library.Packs.OrderByDescending(pack => pack.LastUpdatedUtc).ThenBy(pack => pack.Name)
                    .Select(pack => new PackActivity(pack.Id, pack.Name, pack.UpdateCount, pack.LastUpdatedUtc)).ToArray());
        }
        finally { gate.Release(); }
    }
}
