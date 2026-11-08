namespace StickerApp.Core;

public sealed partial class LibraryStore
{
    public Task SetPackPublicAsync(Guid id, bool value) => MutateAsync(library =>
    {
        var pack = library.Packs.FirstOrDefault(item => item.Id == id)
            ?? throw new InvalidOperationException("This pack no longer exists.");
        if (pack.Public == value) return;
        pack.Public = value;
        Touch(pack);
    });
}
