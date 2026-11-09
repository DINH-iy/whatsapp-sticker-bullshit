using Microsoft.JSInterop;
using StickerApp.Core;

namespace StickerApp.Shared.Services;

public sealed class FriendsPackService(LibraryStore store, IJSRuntime js)
{
    public async Task<int> SyncPublicPacksAsync(Action<string> progress)
    {
        if(FriendsFeature.UnderMaintenance)throw new InvalidOperationException("Friends’ packs is under maintenance and will be added later.");
        await using var module = await js.InvokeAsync<IJSObjectReference>("import", "./_content/StickerApp.Shared/js/friends/friends.js");
        var library = await store.ReadAsync();
        var publicPacks = library.Packs.Where(pack => pack.Public && pack.StickerIds.Count > 0).ToList();
        var mine = await module.InvokeAsync<string[]>("mine");
        foreach (var id in mine.Where(id => !publicPacks.Any(pack => pack.Id.ToString() == id)))
            await module.InvokeVoidAsync("unpublish", id, true);
        foreach (var pack in publicPacks)
        {
            progress($"Sharing {pack.Name}…");
            var path = Path.Combine(Path.GetTempPath(), $"friends-pack-{Guid.NewGuid():N}.zip");
            await using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 65536, FileOptions.DeleteOnClose);
            await store.ExportPackAsync(pack.Id, stream);
            using var reference = new DotNetStreamReference(stream, leaveOpen: true);
            await module.InvokeVoidAsync("publish", pack.Id.ToString(), reference);
            var current = (await store.ReadAsync()).Packs.FirstOrDefault(value => value.Id == pack.Id);
            if (current is null || !current.Public) await module.InvokeVoidAsync("unpublish", pack.Id.ToString(), true);
        }
        return publicPacks.Count;
    }
    public async Task UpdateCollectionAsync(Guid packId)
    {
        if(FriendsFeature.UnderMaintenance)throw new InvalidOperationException("Group updates are under maintenance. Your local edits remain saved.");
        var pack=(await store.ReadAsync()).Packs.Single(pack=>pack.Id==packId);
        var link=pack.Collection??throw new InvalidOperationException("This pack is not a group working copy.");
        var path=Path.Combine(Path.GetTempPath(),$"collection-{Guid.NewGuid():N}.zip");
        await using var stream=new FileStream(path,FileMode.CreateNew,FileAccess.ReadWrite,FileShare.None,65536,FileOptions.DeleteOnClose);
        await store.ExportPackAsync(packId,stream);
        using var reference=new DotNetStreamReference(stream,leaveOpen:true);
        await using var module=await js.InvokeAsync<IJSObjectReference>("import","./_content/StickerApp.Shared/js/friends/friends.js");
        var result=await module.InvokeAsync<StickerApp.Shared.Pages.Friends.GroupCollectionCard>("updateCollection",link.CollectionId.ToString(),link.Revision,link.ServerUrl,reference);
        await store.LinkCollectionAsync(packId,link with {Revision=result.Revision});
    }

    public static async Task UnpublishAsync(IJSRuntime js, Guid id)
    {
        await using var module = await js.InvokeAsync<IJSObjectReference>("import", "./_content/StickerApp.Shared/js/friends/friends.js");
        await module.InvokeVoidAsync("unpublish", id.ToString());
    }
}
