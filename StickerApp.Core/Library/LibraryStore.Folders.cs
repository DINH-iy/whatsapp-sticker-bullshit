namespace StickerApp.Core;

public sealed partial class LibraryStore
{
    public async Task<Guid> CreateFolderAsync(string name)
    {
        var folder = new LibraryFolder { Name = CleanName(name) };
        await MutateAsync(library => library.Folders.Add(folder));
        return folder.Id;
    }
    public Task RenameFolderAsync(Guid id, string name) => MutateAsync(library =>
        (library.Folders.FirstOrDefault(folder => folder.Id == id)
            ?? throw new InvalidOperationException("This folder no longer exists.")).Name = CleanName(name));
    public Task DeleteFolderAsync(Guid id) => MutateAsync(library =>
    {
        library.Folders.RemoveAll(folder => folder.Id == id);
        foreach (var image in library.Stickers.Where(image => image.FolderId == id)) image.FolderId = null;
    });
    public Task MoveToFolderAsync(IEnumerable<Guid> ids, Guid? folderId) => MutateAsync(library =>
    {
        if (folderId is not null && !library.Folders.Any(folder => folder.Id == folderId))
            throw new InvalidOperationException("This folder no longer exists.");
        var selected = ids.ToHashSet();
        foreach (var image in library.Stickers.Where(image => selected.Contains(image.Id))) image.FolderId = folderId;
    });
}
