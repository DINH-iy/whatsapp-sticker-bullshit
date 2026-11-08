namespace StickerApp.Core;

public sealed partial class LibraryStore
{
    public Task LinkCollectionAsync(Guid packId, GroupCollectionLink link) => MutateAsync(library =>
    {
        if (link.CollectionId == Guid.Empty || link.Revision < 1 || !Uri.TryCreate(link.ServerUrl, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("https" or "http") || !string.IsNullOrEmpty(uri.UserInfo) || uri.PathAndQuery != "/")
            throw new InvalidDataException("Invalid group collection link.");
        var pack = library.Packs.Single(pack => pack.Id == packId);
        pack.Collection = link;
        pack.Public = false;
    });
}
