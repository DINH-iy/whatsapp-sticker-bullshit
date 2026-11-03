using System.Text.Json;

namespace StickerApp.Core;

/// <summary>Single-process, atomic file persistence for the first build. Replace behind this service with SQLite later.</summary>
public sealed class LibraryStore
{
    public const int MaxImportBytes = 10 * 1024 * 1024;
    private readonly SemaphoreSlim gate = new(1, 1);
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    public string Root { get; }
    private string Index => Path.Combine(Root, "library.json");
    public LibraryStore(string root)
    {
        Root = Path.GetFullPath(root);
        Directory.CreateDirectory(Path.Combine(Root, "media"));
    }

    public async Task<StickerLibrary> ReadAsync()
    {
        await gate.WaitAsync();
        try { return await ReadInternalAsync(); }
        finally { gate.Release(); }
    }

    private async Task<StickerLibrary> ReadInternalAsync()
    {
        if (!File.Exists(Index)) return new();
        await using var file = File.OpenRead(Index);
        var library = await JsonSerializer.DeserializeAsync<StickerLibrary>(file)
            ?? throw new InvalidDataException("The library is empty or damaged.");
        if (library.SchemaVersion != 1) throw new InvalidDataException("This library needs a newer version of the app.");
        return library;
    }

    private async Task MutateAsync(Action<StickerLibrary> action)
    {
        await gate.WaitAsync();
        try
        {
            var state = await ReadInternalAsync();
            action(state);
            var temp = Index + ".tmp";
            await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(state, Json));
            File.Move(temp, Index, true);
        }
        finally { gate.Release(); }
    }

    public async Task ImportAsync(string name, byte[] bytes)
    {
        if (bytes.Length is 0 or > MaxImportBytes) throw new InvalidDataException("Choose a file smaller than 10 MB.");
        var info = MediaInspector.Inspect(bytes);
        var s = new Sticker { Name = CleanName(Path.GetFileNameWithoutExtension(name)), MimeType = info.MimeType,
            Width = info.Width, Height = info.Height, Animated = info.Animated, Bytes = bytes.Length,
            DurationMs = info.DurationMs, MinimumFrameMs = info.MinimumFrameMs };
        s.FileName = s.Id.ToString("N") + info.Extension;
        var path = MediaPath(s.FileName);
        await File.WriteAllBytesAsync(path, bytes);
        try { await MutateAsync(l => l.Stickers.Add(s)); }
        catch { File.Delete(path); throw; }
    }

    public Task CreatePackAsync(string name, bool animated) => MutateAsync(l =>
    {
        if (l.Packs.Count >= 10) throw new InvalidOperationException("This build supports up to 10 packs.");
        l.Packs.Add(new() { Name = CleanName(name), Animated = animated });
    });

    public Task RenamePackAsync(Guid id, string name) => MutateAsync(l => { var p = l.Packs.Single(p => p.Id == id); p.Name = CleanName(name); Touch(p); });
    public Task DeletePackAsync(Guid id) => MutateAsync(l => l.Packs.RemoveAll(p => p.Id == id));
    public Task FavoriteAsync(Guid id) => MutateAsync(l => { var s = l.Stickers.Single(s => s.Id == id); s.Favorite = !s.Favorite; });
    public Task EditStickerAsync(Guid id, string name, string tags) => MutateAsync(l =>
    {
        var s = l.Stickers.Single(s => s.Id == id); s.Name = CleanName(name); s.Tags = tags.Trim()[..Math.Min(tags.Trim().Length, 256)];
        foreach (var p in l.Packs.Where(p => p.StickerIds.Contains(id))) Touch(p);
    });

    public async Task ReplaceImageAsync(Guid id, byte[] bytes)
    {
        if (bytes.Length is 0 or > MaxImportBytes) throw new InvalidDataException("Maximum file size: 10 MB.");
        var info = MediaInspector.Inspect(bytes);
        var fileName = Guid.NewGuid().ToString("N") + info.Extension;
        var path = MediaPath(fileName);
        await File.WriteAllBytesAsync(path, bytes);
        try
        {
            await MutateAsync(l =>
            {
                var sticker = l.Stickers.Single(s => s.Id == id);
                if (l.Packs.Any(p => p.StickerIds.Contains(id) && p.Animated != info.Animated))
                    throw new InvalidOperationException("Remove the image from its pack before changing between static and animated.");
                sticker.OriginalFileName ??= sticker.FileName;
                sticker.FileName = fileName;
                sticker.MimeType = info.MimeType;
                sticker.Width = info.Width; sticker.Height = info.Height;
                sticker.Bytes = bytes.Length; sticker.Animated = info.Animated;
                sticker.DurationMs = info.DurationMs; sticker.MinimumFrameMs = info.MinimumFrameMs;
                foreach (var pack in l.Packs.Where(p => p.StickerIds.Contains(id))) Touch(pack);
            });
        }
        catch { File.Delete(path); throw; }
    }

    public Task AddToPackAsync(Guid packId, IEnumerable<Guid> ids) => MutateAsync(l =>
    {
        var p = l.Packs.Single(p => p.Id == packId);
        var additions = ids.Distinct().Where(id => !p.StickerIds.Contains(id)).ToList();
        if (p.StickerIds.Count + additions.Count > 30) throw new InvalidOperationException("A pack can contain at most 30 stickers.");
        foreach (var id in additions)
            if (l.Stickers.Single(s => s.Id == id).Animated != p.Animated)
                throw new InvalidOperationException("Use separate packs for static and animated stickers.");
        p.StickerIds.AddRange(additions); Touch(p);
    });

    public Task RemoveFromPackAsync(Guid packId, IEnumerable<Guid> ids) => MutateAsync(l =>
    {
        var p = l.Packs.Single(p => p.Id == packId); var remove = ids.ToHashSet(); p.StickerIds.RemoveAll(remove.Contains); Touch(p);
    });

    public Task ReorderAsync(Guid packId, Guid stickerId, int direction) => MutateAsync(l =>
    {
        var p = l.Packs.Single(p => p.Id == packId); var i = p.StickerIds.IndexOf(stickerId); var j = i + Math.Sign(direction);
        if (i >= 0 && j >= 0 && j < p.StickerIds.Count) { (p.StickerIds[i], p.StickerIds[j]) = (p.StickerIds[j], p.StickerIds[i]); Touch(p); }
    });

    public async Task DeleteStickersAsync(IEnumerable<Guid> ids)
    {
        var remove = ids.ToHashSet(); List<string> files = [];
        await MutateAsync(l =>
        {
            files = l.Stickers.Where(s => remove.Contains(s.Id)).SelectMany(s => new[] { s.FileName, s.OriginalFileName }).OfType<string>().Distinct().ToList();
            l.Stickers.RemoveAll(s => remove.Contains(s.Id));
            foreach (var p in l.Packs.Where(p => p.StickerIds.Any(remove.Contains))) { p.StickerIds.RemoveAll(remove.Contains); Touch(p); }
        });
        foreach (var file in files) File.Delete(MediaPath(file));
    }

    public string MediaPath(string fileName)
    {
        if (fileName != Path.GetFileName(fileName) || string.IsNullOrEmpty(fileName)) throw new InvalidDataException("Invalid media filename.");
        return Path.Combine(Root, "media", fileName);
    }
    public async Task<string> PreviewAsync(Sticker sticker) => $"data:{sticker.MimeType};base64,{Convert.ToBase64String(await File.ReadAllBytesAsync(MediaPath(sticker.FileName)))}";
    private static void Touch(StickerPack p) => p.Version = Guid.NewGuid().ToString("N");
    private static string CleanName(string name) => !string.IsNullOrWhiteSpace(name) && name.Trim().Length <= 128
        ? name.Trim() : throw new InvalidOperationException("Use a name of 1–128 characters.");
}
