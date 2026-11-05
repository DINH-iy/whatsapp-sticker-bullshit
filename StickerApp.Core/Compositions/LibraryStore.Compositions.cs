using System.Text.Json;

namespace StickerApp.Core;

public sealed partial class LibraryStore
{
    public async Task<LayerComposition?> ReadCompositionAsync(Guid stickerId)
    {
        await gate.WaitAsync();
        try
        {
            var image = (await ReadInternalAsync()).Stickers.FirstOrDefault(image => image.Id == stickerId)
                ?? throw new InvalidOperationException("This sticker no longer exists.");
            if (image.CompositionFileName is null) return null;
            var path = MediaPath(image.CompositionFileName);
            if (new FileInfo(path).Length > 12 * 1024 * 1024)
                throw new InvalidDataException("This composition is too large to open.");
            await using var stream = File.OpenRead(path);
            var composition = await JsonSerializer.DeserializeAsync<LayerComposition>(stream)
                ?? throw new InvalidDataException("The composition is empty.");
            composition.Validate();
            return composition;
        }
        finally { gate.Release(); }
    }

    // Publish the image and editable source together. Sources are self-contained PNGs,
    // so removing a source sticker later does not damage an existing composition.
    public async Task<Guid> SaveCompositionAsync(Guid? stickerId, string name, byte[] png,
        LayerComposition composition, Guid? packId = null)
    {
        name = CleanName(name);
        composition.Validate();
        if (png.Length is 0 or > MaxImportBytes) throw new InvalidDataException("The sticker exceeds 10 MB.");
        var info = MediaInspector.Inspect(png);
        if (info.MimeType != "image/png" || info.Animated || info.Width != 512 || info.Height != 512)
            throw new InvalidDataException("The composition must export a still 512 × 512 PNG.");
        await gate.WaitAsync();
        var token = Guid.NewGuid().ToString("N");
        var imageFile = token + ".png";
        var compositionFile = token + ".layers.json";
        var id = stickerId ?? Guid.NewGuid();
        List<string> replacedFiles = [];
        try
        {
            await File.WriteAllBytesAsync(MediaPath(imageFile), png);
            await File.WriteAllTextAsync(MediaPath(compositionFile), JsonSerializer.Serialize(composition, Json));
            await MutateInternalAsync(library =>
            {
                Sticker image;
                if (stickerId is not null)
                    image = library.Stickers.FirstOrDefault(image => image.Id == id)
                        ?? throw new InvalidOperationException("This sticker no longer exists.");
                else
                {
                    image = new Sticker { Id = id };
                    library.Stickers.Add(image);
                    if (packId is not null)
                    {
                        var pack = library.Packs.FirstOrDefault(pack => pack.Id == packId)
                            ?? throw new InvalidOperationException("This pack no longer exists.");
                        if (pack.StickerIds.Count >= 30) throw new InvalidOperationException("A pack can contain at most 30 stickers.");
                        pack.StickerIds.Add(id);
                    }
                }
                replacedFiles = new[] { image.FileName, image.OriginalFileName, image.CompositionFileName }
                    .OfType<string>().Where(file => file.Length > 0).Distinct().ToList();
                image.Name = name;
                image.FileName = imageFile;
                image.OriginalFileName = null;
                image.CompositionFileName = compositionFile;
                image.MimeType = info.MimeType;
                image.Width = info.Width;
                image.Height = info.Height;
                image.Bytes = png.Length;
                image.Animated = false;
                image.DurationMs = image.MinimumFrameMs = 0;
                foreach (var pack in library.Packs.Where(pack => pack.StickerIds.Contains(id))) Touch(pack);
            });
            foreach (var file in replacedFiles)
            {
                try { File.Delete(MediaPath(file)); }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
            }
            return id;
        }
        catch
        {
            foreach (var file in new[] { imageFile, compositionFile })
            {
                try { File.Delete(MediaPath(file)); }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
            }
            throw;
        }
        finally { gate.Release(); }
    }
}
