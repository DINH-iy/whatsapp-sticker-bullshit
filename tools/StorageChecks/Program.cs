using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using StickerApp.Core;

// Dependency-free persistence checks. Fixtures exercise file bookkeeping and header inspection,
// not image decoding or encoder quality. No user library is opened or modified.
var root = Path.Combine(Path.GetTempPath(), "stick-it-storage-checks-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
var png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jRZkAAAAASUVORK5CYII=");
var webp = new byte[38];
"RIFF"u8.CopyTo(webp);
BinaryPrimitives.WriteUInt32LittleEndian(webp.AsSpan(4), 30);
"WEBPVP8X"u8.CopyTo(webp.AsSpan(8));
BinaryPrimitives.WriteUInt32LittleEndian(webp.AsSpan(16), 10);
webp[24] = webp[27] = 255;
webp[25] = webp[28] = 1; // 512 x 512 canvas.
"VP8L"u8.CopyTo(webp.AsSpan(30));
var animated = MediaInspector.AsAnimatedSticker(webp);
var gif = Convert.FromBase64String("R0lGODlhAQABAIAAAAAAAP///yH5BAEAAAAALAAAAAABAAEAAAIBRAA7");
var checks = 0;

try
{
    await Check("duplicates merge pack membership, order, favorites and tags", async store =>
    {
        var firstId = await store.ImportAsync("First.png", png);
        await store.ImportAsync("Second.png", png);
        var images = (await store.ReadAsync()).Stickers;
        Require(images[0].Id == firstId, "Import must return the exact committed sticker ID.");
        await store.CreatePackWithStickerAsync("Pack A", images[0].Id);
        await store.CreatePackWithStickerAsync("Pack B", images[1].Id);
        var packs = (await store.ReadAsync()).Packs;
        await store.AddToPackAsync(packs[0].Id, [images[1].Id]);
        await store.EditStickerAsync(images[0].Id, "First", "cats");
        await store.EditStickerAsync(images[1].Id, "Second", "funny");
        await store.FavoriteAsync(images[1].Id);
        await store.SetStickerNsfwAsync(images[1].Id, true);
        var scan = await store.ScanLibraryAsync();
        Require(scan.Duplicates.Count == 1, "Expected one duplicate group");
        var selection = new DuplicateSelection(scan.Duplicates[0].Fingerprint, images[0].Id, [images[1].Id]);
        Require(await store.RemoveDuplicatesAsync([selection]) == 1, "Expected one removed image");
        var saved = await store.ReadAsync();
        Require(saved.Stickers.Count == 1 && saved.Stickers[0].Favorite, "Favorite was lost");
        Require(saved.Stickers[0].Nsfw, "A duplicate's NSFW tag was lost");
        Require(saved.Stickers[0].Tags == "cats; funny", "Tags were lost");
        Require(saved.Packs.All(pack => pack.StickerIds.SequenceEqual([images[0].Id])), "Pack references were lost or repeated");
        Require(Directory.GetFiles(Path.Combine(store.Root, "media")).Length == 1, "Removed duplicate file remains");
        await Throws(() => store.RemoveDuplicatesAsync([selection]));
    });

    await Check("identical stickers with different sources stay separate; stale scans reject edits", async store =>
    {
        await store.SetOriginalRetentionAsync(true, true);
        await store.ImportAsync("a", png);
        await store.ImportAsync("b", png.Concat(new byte[] { 1 }).ToArray());
        var images = (await store.ReadAsync()).Stickers;
        foreach (var image in images) await store.ReplaceImageAsync(image.Id, webp);
        Require((await store.ScanLibraryAsync()).Duplicates.Count == 0, "Different originals were merged");
        await store.ImportAsync("c", png);
        await store.ImportAsync("d", png);
        var scan = await store.ScanLibraryAsync();
        var group = scan.Duplicates.Single();
        await store.ReplaceImageAsync(group.Images[1].Id, webp);
        await Throws(() => store.RemoveDuplicatesAsync([new(group.Fingerprint, group.Images[0].Id, [group.Images[1].Id])]));
        Require((await store.ReadAsync()).Stickers.Count == 4, "Stale review deleted images");
    });

    await Check("retention, safe preparation and superseded-edit cleanup", async store =>
    {
        await store.ImportAsync("still", png);
        var id = (await store.ReadAsync()).Stickers[0].Id;
        await store.ReplaceImageAsync(id, png.Concat(new byte[] { 2 }).ToArray());
        Require((await store.ReadAsync()).Stickers[0].OriginalFileName is not null, "Recovery source discarded before preparation");
        await store.ReplaceImageAsync(id, webp);
        Require((await store.ReadAsync()).Stickers[0].OriginalFileName is null, "Disabled source was retained");
        Require(Directory.GetFiles(Path.Combine(store.Root, "media")).Length == 1, "Old edits leaked");
        await store.ReplaceImageAsync(id, webp, keepOriginal: true);
        Require((await store.ReadAsync()).Stickers[0].OriginalFileName is not null, "Per-image keep choice ignored");
        await store.ReplaceImageAsync(id, webp, keepOriginal: false);
        Require(Directory.GetFiles(Path.Combine(store.Root, "media")).Length == 1, "Per-image discard choice ignored");
        await store.ImportAsync("animation", gif);
        id = (await store.ReadAsync()).Stickers.Last().Id;
        await store.ReplaceImageAsync(id, animated);
        Require((await store.ReadAsync()).Stickers.Last().OriginalFileName is not null, "Default GIF source not retained");
        await store.ReplaceImageAsync(id, gif, keepOriginal: false);
        await store.ReplaceImageAsync(id, animated);
        var saved = (await store.ReadAsync()).Stickers.Last();
        Require(saved.OriginalFileName is null && saved.OriginalDiscarded, "Discard choice was lost during automatic preparation");
        Require((await store.GetStatisticsAsync()).UnusedFiles == 0, "Save leaked intermediate media");
    });

    await Check("original cleanup protects layers and unfinished images", async store =>
    {
        await store.SetOriginalRetentionAsync(true, true);
        await store.ImportAsync("prepared", png);
        var id = (await store.ReadAsync()).Stickers.Single().Id;
        await store.ReplaceImageAsync(id, webp);
        var library = await store.ReadAsync();
        var source = library.Stickers[0].OriginalFileName!;
        var layers = Guid.NewGuid().ToString("N") + ".layers.json";
        await File.WriteAllTextAsync(store.MediaPath(layers), "{}");
        library.Stickers[0].CompositionFileName = layers;
        await File.WriteAllTextAsync(Path.Combine(store.Root, "library.json"), JsonSerializer.Serialize(library));
        var scan = await store.ScanLibraryAsync();
        Require(scan.Statistics.RemovableOriginals == 1 && scan.Statistics.LayerBytes == 2, "Incorrect source/layer statistics");
        Require(await store.RemoveOriginalsAsync(store.Root, scan.OriginalFiles) == png.Length, "Wrong freed source size");
        Require(File.Exists(store.MediaPath(layers)), "Editable composition was deleted");
        Require(!File.Exists(store.MediaPath(source)), "Source was not removed");
        await store.ImportAsync("unfinished", png);
        id = (await store.ReadAsync()).Stickers.Last().Id;
        await store.ReplaceImageAsync(id, png);
        scan = await store.ScanLibraryAsync();
        await store.RemoveOriginalsAsync(store.Root, scan.OriginalFiles);
        Require((await store.ReadAsync()).Stickers.Last().OriginalFileName is not null, "Unprepared recovery source removed");
    });

    await Check("unused cleanup rechecks references, keeps unknown files, and counts packs once", async store =>
    {
        await store.ImportAsync("image", png);
        var image = (await store.ReadAsync()).Stickers[0];
        await store.CreatePackWithStickerAsync("One", image.Id);
        await store.CreatePackWithStickerAsync("Two", image.Id);
        var orphan = Guid.NewGuid().ToString("N") + ".png";
        await File.WriteAllBytesAsync(store.MediaPath(orphan), png);
        await File.WriteAllTextAsync(store.MediaPath("do-not-delete.txt"), "manual");
        var scan = await store.ScanLibraryAsync();
        Require(scan.Statistics.CurrentBytes == png.Length && scan.Statistics.PackStorage.All(pack => pack.StickerBytes == png.Length), "Shared images double counted in library");
        Require(scan.Statistics.StorageSegments.Sum(segment => segment.Bytes) == scan.Statistics.MediaBytes + scan.Statistics.IndexBytes, "Total storage segments do not match disk usage");
        Require(scan.Statistics.StorageSegments.Single(segment => segment.Label == "Shared between packs").Bytes == png.Length, "Shared pack files counted twice in total bar");
        Require(scan.UnusedFiles.SequenceEqual([orphan]), "Unknown file offered for deletion");
        var library = await store.ReadAsync();
        library.Stickers[0].OriginalFileName = orphan;
        await File.WriteAllTextAsync(Path.Combine(store.Root, "library.json"), JsonSerializer.Serialize(library));
        Require(await store.RemoveUnusedFilesAsync(store.Root, scan.UnusedFiles) == 0, "Now-referenced file deleted");
        await Throws(() => store.RemoveUnusedFilesAsync(store.Root + "-other", scan.UnusedFiles));
    });

    await Check("storage cap rejects growth and allows space-saving preparation and deletion", async store =>
    {
        await store.SetStorageLimitAsync(1024 * 1024);
        var oversized = png.Concat(new byte[2 * 1024 * 1024]).ToArray();
        await Throws(() => store.ImportAsync("too big", oversized));
        Require((await store.ReadAsync()).Stickers.Count == 0 && !Directory.GetFiles(Path.Combine(store.Root, "media")).Any(), "Rejected import left data behind");
        await store.SetStorageLimitAsync(null);
        await store.ImportAsync("big", oversized);
        await store.SetStorageLimitAsync(1024 * 1024);
        var id = (await store.ReadAsync()).Stickers[0].Id;
        await store.ReplaceImageAsync(id, webp);
        Require((await store.GetStatisticsAsync()).MediaBytes == webp.Length, "Space-saving replacement rejected or source leaked");
        await store.DeleteStickersAsync([id]);
        Require((await store.ReadAsync()).Stickers.Count == 0, "Deletion blocked by quota");
    });

    await Check("per-import choices override defaults and survive preparation", async store =>
    {
        await store.SetOriginalRetentionAsync(false, true);
        await store.ImportAsync("keep still", png, keepOriginal: true);
        await store.ImportAsync("discard GIF", gif, keepOriginal: false);
        var images = (await store.ReadAsync()).Stickers;
        await store.ReplaceImageAsync(images[0].Id, webp);
        await store.ReplaceImageAsync(images[1].Id, animated);
        images = (await store.ReadAsync()).Stickers;
        Require(images[0].OriginalFileName is not null && images[1].OriginalFileName is null, "Per-file import choices were ignored");
        var batchSource = Path.Combine(store.DefaultRoot, "batch.png");
        await File.WriteAllBytesAsync(batchSource, png);
        await store.ImportBatchAsync([new("keep batch", batchSource, true), new("discard batch", batchSource, false)]);
        images = (await store.ReadAsync()).Stickers;
        await store.ReplaceImageAsync(images[2].Id, webp);
        await store.ReplaceImageAsync(images[3].Id, webp);
        images = (await store.ReadAsync()).Stickers;
        Require(images[2].OriginalFileName is not null && images[3].OriginalFileName is null, "Batch choices were ignored");
        Require((await store.GetStatisticsAsync()).UnusedFiles == 0, "Import preparation leaked media");
    });

    await Check("publisher validation, versioning and both export formats", async store =>
    {
        var id = await store.CreatePackAsync("Publisher test", " Alice ");
        await store.ImportAsync("sticker", webp, id);
        var initial = (await store.ReadAsync()).Packs.Single();
        Require(initial.Publisher == "Alice", "Publisher was not trimmed/saved on creation");
        await store.UpdatePackDetailsAsync(id, "Renamed pack", "Studio Example");
        var saved = (await store.ReadAsync()).Packs.Single();
        Require(saved.Name == "Renamed pack" && saved.Publisher == "Studio Example" && saved.Version != initial.Version, "Details edit was not saved/versioned");
        foreach (var invalid in new[] { "", "   ", new string('a',129), "one\ntwo" })
            await Throws(() => store.UpdatePackDetailsAsync(id, "Invalid name change", invalid));
        Require((await store.ReadAsync()).Packs.Single().Name == "Renamed pack", "Invalid details partially changed the pack");
        using var backup = new MemoryStream();
        await store.ExportPackAsync(id, backup);
        var imported = await PackArchive.ReadAsync(backup);
        Require(imported.Publisher == "Studio Example", "Backup export lost the publisher");
        backup.Position = 0;
        var importedId = await store.ImportPackAsync(backup);
        Require((await store.ReadAsync()).Packs.Single(pack => pack.Id == importedId).Publisher == "Studio Example", "Backup import lost the publisher");
        var cover = (byte[])png.Clone();
        BinaryPrimitives.WriteInt32BigEndian(cover.AsSpan(16),96);
        BinaryPrimitives.WriteInt32BigEndian(cover.AsSpan(20),96);
        using var exported = new MemoryStream();
        await store.ExportPackAsync(id, exported, cover);
        using var archive = new ZipArchive(exported, ZipArchiveMode.Read, leaveOpen:true);
        using var author = new StreamReader(archive.GetEntry("author.txt")!.Open());
        Require((await author.ReadToEndAsync()).Trim() == "Studio Example", "WhatsApp archive author was not updated");
    });

    await Check("combined filters distinguish file bytes, stored bytes and dimensions without reordering packs", store =>
    {
        var still = new Sticker { Name="Zulu", Tags="cats", Width=1024,Height=512,MimeType="image/png",Bytes=200 };
        var animation = new Sticker { Name="Alpha", Width=512,Height=512,MimeType="image/webp",Animated=true,Bytes=300 };
        var portrait = new Sticker { Name="Beta", Width=256,Height=512,MimeType="image/jpeg",Bytes=100,CompositionFileName="layers.json" };
        var mixed = new StickerPack { Name="Mixed",Publisher="Alice",StickerIds=[still.Id,animation.Id] };
        var onlyStill = new StickerPack { Name="Photos",Publisher="Bob",StickerIds=[portrait.Id] };
        var empty = new StickerPack { Name="Empty",Publisher="Alice" };
        var library = new StickerLibrary {Stickers=[still,animation,portrait],Packs=[mixed,onlyStill,empty]};
        var snapshot = new LibraryBrowseSnapshot(library,
            new Dictionary<Guid,ImageStorageSize> { [still.Id]=new(200,2_000_000),[animation.Id]=new(300,300),[portrait.Id]=new(100,100) },
            new Dictionary<Guid,PackStorageStatistics> { [mixed.Id]=new(mixed.Id,mixed.Name,2,500,1_999_800),[onlyStill.Id]=new(onlyStill.Id,onlyStill.Name,1,100,0),[empty.Id]=new(empty.Id,empty.Name,0,0,0) });
        Require(LibraryBrowsing.Images(snapshot,new(Sort:"file-desc"),true)[0].Id == animation.Id, "File-byte sort used source size");
        Require(LibraryBrowsing.Images(snapshot,new(Sort:"storage-desc"),true)[0].Id == still.Id, "Stored-byte sort ignored originals");
        Require(LibraryBrowsing.Images(snapshot,new(Sort:"pixels-desc"),true)[0].Id == still.Id, "Pixel dimensions sort failed");
        Require(LibraryBrowsing.Images(snapshot,new(Search:" cats ",Type:"still",Format:"png",Dimensions:"landscape",Storage:"large"),true).Single().Id==still.Id,"Combined image filters failed");
        Require(LibraryBrowsing.Images(snapshot,new(Type:"animated",Format:"webp",Dimensions:"512"),true).Single().Id==animation.Id,"Prepared GIF was not classified as animated");
        Require(LibraryBrowsing.Images(snapshot,new(Type:"layered"),true).Single().Id==portrait.Id,"Layered filter failed");
        Require(LibraryBrowsing.Images(snapshot,new(Type:"animated",Format:"png"),true).Count==0,"Incompatible filters returned images");
        Require(LibraryBrowsing.Packs(snapshot,new(Search:"alice",Type:"mixed",Storage:"medium"),true).Single().Id==mixed.Id,"Combined pack filters failed");
        Require(LibraryBrowsing.Packs(snapshot,new(Count:"empty"),true).Single().Id==empty.Id,"Empty-pack filter failed");
        Require(LibraryBrowsing.Packs(snapshot,new(Sort:"storage-desc"),true)[0].Id==mixed.Id,"Pack storage sort failed");
        LibraryBrowsing.Images(snapshot,new(Sort:"name-asc"),true,mixed.Id);
        Require(mixed.StickerIds.SequenceEqual([still.Id,animation.Id]),"Viewing a sort changed saved pack order");
        Require(LibraryBrowsing.Images(snapshot,new(),true,mixed.Id).Select(image=>image.Id).SequenceEqual(mixed.StickerIds),"Default pack order was not restored");
        return Task.CompletedTask;
    });

    await Check("finite WebP previews loop without changing stored frames or exports", async store =>
    {
        var finite = (byte[])animated.Clone();
        var animationHeader = finite.AsSpan().IndexOf("ANIM"u8);
        BinaryPrimitives.WriteUInt16LittleEndian(finite.AsSpan(animationHeader + 12),1);
        var originalHash = SHA256.HashData(finite);
        var preview = MediaInspector.ForLoopingPreview(finite);
        Require(BinaryPrimitives.ReadUInt16LittleEndian(preview.AsSpan(animationHeader + 12)) == 0, "Finite preview did not loop");
        Require(SHA256.HashData(finite).SequenceEqual(originalHash), "Preview mutated the input bytes");
        Require(MediaInspector.Inspect(preview) == MediaInspector.Inspect(finite), "Preview changed animation frames/duration/size");
        Require(preview.Where((value,index) => value != finite[index]).Count()==1, "Preview changed more than the loop field");
        Require(ReferenceEquals(MediaInspector.ForLoopingPreview(preview),preview), "Already-looping preview was needlessly copied");
        await store.ImportAsync("finite animation",finite);
        var image = (await store.ReadAsync()).Stickers.Single();
        var url = await store.PreviewAsync(image);
        Require(Convert.FromBase64String(url[(url.IndexOf(',')+1)..]).SequenceEqual(preview), "Library did not use the looping preview");
        Require((await File.ReadAllBytesAsync(store.MediaPath(image.FileName))).SequenceEqual(finite), "Library preview changed saved data");
        Require(WhatsAppSticker.PrepareForExport(finite).SequenceEqual(finite), "Export changed a valid animation");
    });

    if (args.Length > 0)
    {
        await Check("supplied animated pack keeps all bytes, frames and timings through import", async store =>
        {
            await using var input = File.OpenRead(args[0]);
            var source = await PackArchive.ReadAsync(input);
            input.Position=0;
            var packId = await store.ImportPackAsync(input);
            var library = await store.ReadAsync();
            var pack = library.Packs.Single(pack=>pack.Id==packId);
            Require(pack.StickerIds.Count==source.Images.Count,"Import dropped stickers");
            for(var i=0;i<source.Images.Count;i++)
            {
                var image = library.Stickers.Single(image=>image.Id==pack.StickerIds[i]);
                var bytes = await File.ReadAllBytesAsync(store.MediaPath(image.FileName));
                Require(bytes.SequenceEqual(source.Images[i].Bytes),"Import changed sticker bytes");
                Require(MediaInspector.Inspect(bytes)==source.Images[i].Info,"Import changed animation metadata");
                var preview = await store.PreviewAsync(image);
                var display = Convert.FromBase64String(preview[(preview.IndexOf(',')+1)..]);
                Require(display.SequenceEqual(MediaInspector.ForLoopingPreview(bytes)),"Imported animation used a non-looping preview");
            }
        });
    }

    await Check("edited WebP layers survive save, reopen and reuse without a GIF original", async store =>
    {
        var source = "data:image/webp;base64," + Convert.ToBase64String(animated);
        var composition = new LayerComposition { Layers = [new CompositionLayer {
            Id = "edited-animation", Name = "Cutout", Source = "data:image/png;base64," + Convert.ToBase64String(png),
            GifSource = source, GifDuration = 1, TrimStart = 0, TrimEnd = 1, Opacity = 0.5, Hue = 80, ColorStrength = 0.4, BorderWidth = 6, BorderColor = "#44ee77"
        }] };
        var id = await store.SaveCompositionAsync(null, "Animated cutout", animated, composition);
        var reopened = await new LibraryStore(store.DefaultRoot).ReadCompositionAsync(id);
        Require(reopened?.Layers.Single().GifSource == source, "Edited animation source changed on save");
        Require(reopened!.Layers.Single().Opacity == 0.5, "Layer opacity was lost");
        Require(reopened.Layers.Single().Hue == 80 && reopened.Layers.Single().ColorStrength == 0.4, "Layer hue or strength was lost");
        Require(reopened.Layers.Single().BorderWidth == 6 && reopened.Layers.Single().BorderColor == "#44ee77", "Object outline was lost on save/reopen");
        reopened.Layers[0].BorderWidth = double.NaN;
        var invalidOutlineRejected = false;
        try { reopened.Validate(); } catch (InvalidDataException) { invalidOutlineRejected = true; }
        Require(invalidOutlineRejected, "Non-finite object outline width was accepted");
        Require(await store.ReadLayerAnimationAsync(id) == source, "Animated WebP cannot be reused as a layer");
        await store.ImportAsync("Original GIF", gif, keepOriginal: true);
        var imported = (await store.ReadAsync()).Stickers.Single(image => image.Name == "Original GIF");
        await store.ReplaceImageAsync(imported.Id, animated, keepOriginal: true);
        Require(await store.ReadLayerAnimationAsync(imported.Id) == source, "Layer reuse opened the backup GIF instead of the current edited animation");
        composition.Layers[0].GifSource = "data:image/webp;base64," + Convert.ToBase64String(webp);
        var rejected = false;
        try { await store.SaveCompositionAsync(id, "Invalid still animation", animated, composition); }
        catch (InvalidDataException) { rejected = true; }
        Require(rejected, "A still WebP was accepted as an animation source");
    });

    await Check("custom fonts persist, deduplicate, count toward storage, and survive cleanup", async store =>
    {
        var bytes = await File.ReadAllBytesAsync("StickerApp.Shared/wwwroot/fonts/Anton-Regular.ttf");
        var font = await store.ImportFontAsync("My meme font.ttf", bytes);
        var duplicate = await store.ImportFontAsync("Duplicate.ttf", bytes);
        Require(font.Id == duplicate.Id, "Identical fonts must not consume storage twice");
        var reopened = new LibraryStore(store.DefaultRoot);
        var fonts = await reopened.ReadFontsAsync();
        Require(fonts.Count == 1 && fonts[0].Data == font.Data, "Imported font changed after reopening");
        var stats = await store.GetStatisticsAsync();
        Require(stats.FontBytes == bytes.Length && stats.Fonts.Single().Bytes == bytes.Length, "Font sizes must be reported exactly");
        Require(stats.StorageSegments.Single(s => s.Label == "Custom fonts").Bytes == bytes.Length, "Fonts need their own storage segment");
        Require(stats.StorageSegments.Sum(s => s.Bytes) == stats.MediaBytes + stats.IndexBytes, "Font storage must not be counted twice");
        var scan = await store.ScanLibraryAsync();
        Require(!scan.UnusedFiles.Any(f => f.EndsWith(".font")), "Imported fonts must be protected from cleanup");
        var text = new TextLayerOptions {Content="TOP TEXT 😎",Font=font.Id,FontData=font.Data,Gradient=true,Bold=true,Underline=true};
        var composition = new LayerComposition {Layers=[new(){Id="caption",Name="Caption",Source="data:image/png;base64,"+Convert.ToBase64String(png),Text=text,Rotation=30}]};
        var id = await store.SaveCompositionAsync(null,"Text sticker",webp,composition, nsfw: true);
        Require((await reopened.ReadAsync()).Stickers.Single(s => s.Id == id).Nsfw, "The layered sticker spoiler tag was lost");
        var caption = (await reopened.ReadCompositionAsync(id))!.Layers.Single();
        Require(caption.Text!.Content == text.Content && caption.Text.FontData == font.Data && caption.Text.Gradient && caption.Text.Underline && caption.Rotation == 30, "Editable text, emoji, formatting, font or transform was lost");
        var rejected=false;try{await store.ImportFontAsync("Bad.ttf",png);}catch(InvalidDataException){rejected=true;}
        Require(rejected,"Non-font data must be rejected");
        var count=(await store.ReadAsync()).Fonts.Count;
        await store.SetStorageLimitAsync(1024*1024);
        var big = new byte[2*1024*1024];bytes.CopyTo(big,0);
        rejected=false;try{await store.ImportFontAsync("Over budget.ttf",big);}catch(InvalidOperationException){rejected=true;}
        Require(rejected && (await store.ReadAsync()).Fonts.Count == count,"An over-budget font import must leave no library entry");
        Require(Directory.GetFiles(Path.Combine(store.Root,"media"),"*.font").Length == count,"An over-budget font must leave no file");
    });

    await Check("NSFW tags, pack inheritance, editable backups and blur preferences persist independently", async store =>
    {
        Require(store.BlurNsfwPreviews, "NSFW blurring must default on");
        var id = await store.ImportAsync("Tagged image", png);
        var other = await store.ImportAsync("Other image", png);
        await store.CreatePackWithStickerAsync("Spoiler pack", id);
        var packId = (await store.ReadAsync()).Packs.Single().Id;
        await store.SetPackNsfwAsync(packId, true);
        var library = await store.ReadAsync();
        Require(library.IsNsfw(library.Stickers.Single(s => s.Id == id)) && !library.IsNsfw(library.Stickers.Single(s => s.Id == other)), "Pack NSFW must apply only to its members");
        await store.SetPackNsfwAsync(packId, false);
        await store.SetStickerNsfwAsync(id, true);
        library = await store.ReadAsync();
        Require(library.IsNsfw(library.Packs.Single()), "An NSFW sticker must protect its pack's preview");
        await store.SetPackNsfwAsync(packId, true);
        library = await store.ReadAsync();
        using var archive = new MemoryStream();
        await PackArchive.WriteAsync(archive, library.Packs.Single(), [library.Stickers.Single(s => s.Id == id)], sticker => File.OpenRead(Path.Combine(store.Root, "media", sticker.FileName)));
        archive.Position = 0;
        var imported = await PackArchive.ReadAsync(archive);
        Require(imported.Nsfw && imported.Images.Single().Nsfw, "Editable backups lost spoiler tags");
        archive.Position = 0;
        var importedId = await store.ImportPackAsync(archive);
        library = await store.ReadAsync();
        Require(library.Packs.Single(p => p.Id == importedId).Nsfw, "Restoring an editable backup lost the pack tag");
        await store.SetBlurNsfwPreviewsAsync(false);
        await store.SetOriginalRetentionAsync(true, true);
        await store.SetAskForOriginalOnImportAsync(false);
        await store.SetDonutStorageChartAsync(true);
        await store.SetDecimalStorageUnitsAsync(true);
        await store.SetStorageLimitAsync(1024 * 1024);
        var reopened = new LibraryStore(store.DefaultRoot);
        Require(!reopened.BlurNsfwPreviews && (await reopened.ReadAsync()).Stickers.Single(s => s.Id == id).Nsfw, "Other preferences or a restart removed the blur preference or tag");
        await reopened.SetBlurNsfwPreviewsAsync(true);
        Require(new LibraryStore(store.DefaultRoot).BlurNsfwPreviews, "Re-enabling blur was not saved");
    });

    await Check("Pinterest progress counts completed downloads, estimates remaining time and separates saving", async store =>
    {
        using var http = new HttpClient(new FixtureHttpHandler((request, token) =>
            Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new ByteArrayContent(png) })));
        using var importer = new PinterestImportClient(http);
        var images = Enumerable.Range(1, 4).Select(i => new PinterestImage(i.ToString(), "", $"Pin {i}",
            $"https://i.pinimg.com/{i}.png", $"https://i.pinimg.com/{i}.png")).ToList();
        List<PinterestImportProgress> updates = [];
        await importer.ImportAsync(store, images, progress: new FixtureProgress<PinterestImportProgress>(updates.Add));
        Require(updates.Select(p => p.Completed).SequenceEqual([0, 1, 2, 3, 4]), "Download progress must advance once per validated, staged image");
        Require(updates.All(p => p.Total == 4), "The batch total changed");
        Require(updates.Take(2).All(p => p.EstimatedRemaining is null), "ETA must wait for two downloads");
        Require(updates[2].EstimatedRemaining is { Ticks: > 0 } && updates[3].EstimatedRemaining is { Ticks: > 0 }, "Active downloads must estimate remaining time");
        Require(updates.Take(4).All(p => !p.IsSaving) && updates[^1].IsSaving && updates[^1].EstimatedRemaining is null, "Saving must be separate from the download estimate");
        Require((await store.ReadAsync()).Stickers.Count == 4, "The complete batch was not saved");
    });

    await Check("cancelled Pinterest downloads stop progress and leave no partial library import", async store =>
    {
        using var http = new HttpClient(new FixtureHttpHandler((request, token) =>
            Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new ByteArrayContent(png) })));
        using var importer = new PinterestImportClient(http);
        using var cancellation = new CancellationTokenSource();
        var images = Enumerable.Range(1, 4).Select(i => new PinterestImage(i.ToString(), "", $"Pin {i}",
            $"https://i.pinimg.com/{i}.png", $"https://i.pinimg.com/{i}.png")).ToList();
        List<PinterestImportProgress> updates = [];
        var cancelled = false;
        try
        {
            await importer.ImportAsync(store, images, progress: new FixtureProgress<PinterestImportProgress>(value =>
            { updates.Add(value); if (value.Completed == 2) cancellation.Cancel(); }), cancellationToken: cancellation.Token);
        }
        catch (OperationCanceledException) { cancelled = true; }
        Require(cancelled && updates[^1].Completed == 2 && !updates[^1].IsSaving, "Cancellation must stop before completing the batch");
        Require((await store.ReadAsync()).Stickers.Count == 0, "A cancelled batch left partial imports");
    });

    await Check("Pinterest board preflight enforces full size, complete pagination and explicit unavailable counts", async store =>
    {
        Require(PinterestShareLink.TryParse("https://www.pinterest.com/user/crochet/", out _), "Public board URLs must be accepted");
        foreach (var url in new[] { "https://www.pinterest.com/pin/123/", "https://evil.test/user/board/", "https://www.pinterest.com:8443/user/board/", "https://user:pass@www.pinterest.com/user/board/" })
            Require(!PinterestShareLink.TryParse(url, out _), "Unsafe or non-board URL accepted");
        var image = new PinterestImage("e1_fixture-123", "", "Photo", "https://i.pinimg.com/a.png", "https://i.pinimg.com/a.png");
        var preview = new PinterestSelection("https://www.pinterest.com/user/crochet/", 2, 1, [image])
        { SourceKind = "board", CollectionComplete = true, UnavailableCount = 1 };
        PinterestImportClient.ValidateSelection(preview);
        foreach (var invalid in new[] { preview with { ExpectedCount = 101, UnavailableCount = 100 }, preview with { CollectionComplete = false }, preview with { UnavailableCount = 0 }, preview with { SourceKind = "selection" } })
        {
            var rejected = false; try { PinterestImportClient.ValidateSelection(invalid); } catch (InvalidDataException) { rejected = true; }
            Require(rejected, "Oversized, incomplete or silently partial preview was accepted");
        }
        foreach (var videoUrl in new[] { "https://evil.test/a.mp4", "http://v1.pinimg.com/a.mp4", "https://v1.pinimg.com/a.m3u8", "https://user:pass@v1.pinimg.com/a.mp4", "https://v1.pinimg.com:8443/a.mp4" })
        {
            var rejected = false; try { PinterestImportClient.ValidateSelection(preview with { Images = [image with { MediaType = "video", VideoUrl = videoUrl }] }); } catch (InvalidDataException) { rejected = true; }
            Require(rejected, "An unsafe video URL was accepted");
        }
        await Task.CompletedTask;
    });

    await Check("mixed Pinterest batches review video clips before one atomic image/animation commit", async store =>
    {
        var mp4 = new byte[24]; "ftyp"u8.CopyTo(mp4.AsSpan(4));
        using var http = new HttpClient(new FixtureHttpHandler((request, token) =>
            Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            { Content = new ByteArrayContent(request.RequestUri!.Host == "v1.pinimg.com" ? mp4 : png) })));
        using var importer = new PinterestImportClient(http);
        var image = new PinterestImage("1", "", "Photo", "https://i.pinimg.com/a.png", "https://i.pinimg.com/a.png");
        var video = new PinterestImage("e1_video", "", "Video", "https://i.pinimg.com/b.jpg", "https://i.pinimg.com/b.jpg")
        { MediaType = "video", VideoUrl = "https://v1.pinimg.com/a.mp4" };
        List<PinterestImportProgress> updates = [];
        var reviewed = false;
        await importer.ImportAsync(store, [image, video], progress: new FixtureProgress<PinterestImportProgress>(updates.Add), convertVideo: async (item, path, token) =>
        {
            Require(item.IsVideo && File.Exists(path), "The video must be staged before review");
            Require((await store.ReadAsync()).Stickers.Count == 0, "Photos were saved before video review completed");
            reviewed = true; return animated;
        });
        var library = await store.ReadAsync();
        Require(reviewed && library.Stickers.Count == 2 && library.Stickers.Count(s => s.Animated) == 1, "The mixed batch lost its image or animation");
        Require(updates.Any(p => p.IsConvertingVideo && p.Completed == 1) && updates[^1].IsSaving, "Video-review and save stages must be reported separately");
    });

    await Check("Pinterest video size limits and cancelled review leave no partial imports", async store =>
    {
        var mp4 = new byte[24]; "ftyp"u8.CopyTo(mp4.AsSpan(4));
        var image = new PinterestImage("1", "", "Photo", "https://i.pinimg.com/a.png", "https://i.pinimg.com/a.png");
        var video = new PinterestImage("2", "", "Video", "https://i.pinimg.com/b.jpg", "https://i.pinimg.com/b.jpg")
        { MediaType = "video", VideoUrl = "https://v1.pinimg.com/a.mp4" };
        var oversized = true; var reviews = 0;
        using var http = new HttpClient(new FixtureHttpHandler((request, token) =>
        {
            var content = new ByteArrayContent(request.RequestUri!.Host == "v1.pinimg.com" ? mp4 : png);
            if (oversized && request.RequestUri.Host == "v1.pinimg.com") content.Headers.ContentLength = PinterestImportClient.MaxVideoBytes + 1;
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = content });
        }));
        using var importer = new PinterestImportClient(http);
        var rejected = false;
        try { await importer.ImportAsync(store, [image, video], convertVideo: (_, _, _) => { reviews++; return Task.FromResult(animated); }); }
        catch (InvalidDataException) { rejected = true; }
        Require(rejected && reviews == 0 && (await store.ReadAsync()).Stickers.Count == 0, "An oversized video must reject before review and commit");
        oversized = false; var cancelled = false;
        try { await importer.ImportAsync(store, [image, video], convertVideo: (_, _, _) => Task.FromCanceled<byte[]>(new CancellationToken(true))); }
        catch (OperationCanceledException) { cancelled = true; }
        Require(cancelled && (await store.ReadAsync()).Stickers.Count == 0, "Cancelling clip review left partial photos");
    });

    await Check("KLIPY native download imports the original GIF into its destination pack", async store =>
    {
        await store.SetOriginalRetentionAsync(true, false);
        var packId = await store.CreatePackAsync("KLIPY imports");
        var url = "https://static1.klipy.com/example.gif?delivery=keep-this";
        using var client = new HttpClient(new FixtureHttpHandler((request, token) =>
        {
            Require(request.RequestUri!.OriginalString == url, "Provider URL was changed");
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new ByteArrayContent(gif) });
        }));
        var bytes = await KlipyGifDownloadClient.DownloadAsync(client, url);
        Require(bytes.SequenceEqual(gif), "GIF bytes changed during download");
        await store.ImportAsync("Imported GIF.gif", bytes, packId);
        var library = await store.ReadAsync();
        Require(library.Stickers.Count == 1 && library.Packs.Single().StickerIds.Single() == library.Stickers.Single().Id, "Import or pack membership missing");
        Require(library.Stickers.Single().KeepOriginalOverride == false, "GIF retention default was ignored");
    });

    await Check("KLIPY native download rejects unsafe, invalid, oversized and cancelled responses", async store =>
    {
        var requests = 0;
        using var client = new HttpClient(new FixtureHttpHandler((request, token) =>
        {
            requests++;
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new ByteArrayContent(png) });
        }));
        foreach (var url in new[] { "http://static.klipy.com/a.gif", "https://evil.example/a.gif", "https://static.klipy.com.evil.example/a.gif", "https://user@static.klipy.com/a.gif", "https://static.klipy.com:8443/a.gif" })
            await RejectDownload(() => KlipyGifDownloadClient.DownloadAsync(client, url));
        Require(requests == 0, "Unsafe URLs reached the network");
        await RejectDownload(() => KlipyGifDownloadClient.DownloadAsync(client, "https://static.klipy.com/not-a-gif"));
        using var oversized = new HttpClient(new FixtureHttpHandler((request, token) =>
        {
            var content = new ByteArrayContent(gif); content.Headers.ContentLength = LibraryStore.MaxImportBytes + 1;
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = content });
        }));
        await RejectDownload(() => KlipyGifDownloadClient.DownloadAsync(oversized, "https://static.klipy.com/too-large.gif"));
        using var unknownSize = new HttpClient(new FixtureHttpHandler((request, token) =>
        {
            var content = new StreamContent(new MemoryStream(new byte[LibraryStore.MaxImportBytes + 1]));
            content.Headers.ContentLength = null;
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = content });
        }));
        await RejectDownload(() => KlipyGifDownloadClient.DownloadAsync(unknownSize, "https://static.klipy.com/unknown-size.gif"));
        using var pending = new HttpClient(new FixtureHttpHandler(async (request, token) =>
        {
            await Task.Delay(Timeout.Infinite, token); throw new InvalidOperationException();
        }));
        using var cancellation = new CancellationTokenSource();
        var operation = KlipyGifDownloadClient.DownloadAsync(pending, "https://static.klipy.com/cancel.gif", cancellation.Token);
        cancellation.Cancel();
        var cancelled = false;
        try { await operation; } catch (OperationCanceledException) { cancelled = true; }
        Require(cancelled && (await store.ReadAsync()).Stickers.Count == 0, "Cancelled download modified the library");
    });

    await Check("preferences persist and survive library relocation", async store =>
    {
        await store.SetDonutStorageChartAsync(true);
        await store.SetAskForOriginalOnImportAsync(false);
        await store.SetOriginalRetentionAsync(true, false);
        await store.SetStorageLimitAsync(5 * 1024 * 1024);
        await store.SetDecimalStorageUnitsAsync(true);
        var reopened = new LibraryStore(store.DefaultRoot);
        Require(reopened.DonutStorageChart && !reopened.AskForOriginalOnImport, "Chart style or skip-prompt preference was lost");
        Require(reopened.DecimalStorageUnits && reopened.StorageLimitBytes == 5 * 1024 * 1024, "Display preference changed the byte limit or was not persisted");
        Require(reopened.KeepOriginalStillImages && !reopened.KeepOriginalAnimations && reopened.StorageLimitBytes == 5 * 1024 * 1024, "Preferences not persisted");
        var destination = store.Root + "-moved";
        await store.ChangeFolderAsync(destination);
        reopened = new LibraryStore(store.DefaultRoot);
        Require(reopened.DonutStorageChart && !reopened.AskForOriginalOnImport, "Relocation lost chart/import preferences");
        Require(reopened.DecimalStorageUnits, "Relocation lost the storage-unit preference");
        Require(reopened.Root == destination && reopened.KeepOriginalStillImages && !reopened.KeepOriginalAnimations && reopened.StorageLimitBytes == 5 * 1024 * 1024, "Relocation lost preferences");
    });
    await Check("pack details and cover commit together and preserve untouched covers", async store =>
    {
        var id = await store.CreatePackAsync("Before", "Publisher");
        await store.UpdatePackDetailsAsync(id, "After", "New publisher", true, true, png);
        var reopened = new LibraryStore(store.DefaultRoot);
        var pack = (await reopened.ReadAsync()).Packs.Single();
        Require(pack.Name == "After" && pack.Publisher == "New publisher" && pack.Nsfw && pack.CoverImage == PackCover.FromBytes(png), "Combined details and cover did not persist");
        await reopened.UpdatePackDetailsAsync(id, "Renamed", "New publisher");
        Require((await reopened.ReadAsync()).Packs.Single().CoverImage == pack.CoverImage, "Saving details cleared an untouched cover");
        try
        {
            await reopened.UpdatePackDetailsAsync(id, "Must not save", "Wrong", false, true, [1, 2, 3]);
            throw new Exception("Invalid cover was accepted");
        }
        catch (InvalidDataException) { }
        Require((await reopened.ReadAsync()).Packs.Single().Name == "Renamed", "Invalid cover partially saved details");
        await reopened.UpdatePackDetailsAsync(id, "Removed", "Publisher", false, true, null);
        Require((await new LibraryStore(store.DefaultRoot).ReadAsync()).Packs.Single().CoverImage is null, "Cover removal did not persist");
    });
    await Check("local profile survives relocation and is excluded from pack archives", async store =>
    {
        Require(!(await store.ReadProfileAsync()).SetupComplete, "First launch should offer setup");
        await store.SaveProfileAsync(new("  public-name  ", "Private first name", PackCover.FromBytes(png), true));
        var profile = await new LibraryStore(store.DefaultRoot).ReadProfileAsync();
        Require(profile.Username == "public-name" && profile.SetupComplete && profile.Picture is not null, "Profile did not persist");
        var id = await store.CreatePackAsync("Private by default", profile.Username);
        Require(!(await store.ReadAsync()).Packs.Single().Public, "New packs should default to private");
        await store.UpdatePackDetailsAsync(id, "Public choice", profile.Username, isPublic: true);
        await using var archive = new MemoryStream();
        await store.ExportPackAsync(id, archive);
        archive.Position = 0;
        var importedPack = await PackArchive.ReadAsync(archive);
        Require(importedPack.Public, "Editable pack backup lost public choice");
        using (var zip = new ZipArchive(archive, ZipArchiveMode.Read, true))
        using (var reader = new StreamReader(zip.GetEntry("manifest.json")!.Open()))
            Require(!(await reader.ReadToEndAsync()).Contains("Private first name"), "Private profile leaked into pack export");
        await store.ChangeFolderAsync(store.Root + "-profile-moved");
        Require((await new LibraryStore(store.DefaultRoot).ReadProfileAsync()).Username == "public-name", "Relocation lost the profile");
    });
    await Check("profile overview counts unique stickers and preserves identity across edits and relocation", async store =>
    {
        var first = await store.ReadProfileOverviewAsync();
        Require(first.Identity.Id != Guid.Empty && first.Statistics.Stickers == 0, "New profile identity or empty stats are invalid");
        var stillId = await store.ImportAsync("Still.png", png);
        await store.ImportAsync("Animation.gif", gif);
        await store.FavoriteAsync(stillId);
        var publicId = await store.CreatePackAsync("Public", "Publisher", isPublic: true);
        var privateId = await store.CreatePackAsync("Private", "Publisher");
        await store.AddToPackAsync(publicId, [stillId]);
        await store.AddToPackAsync(privateId, [stillId]);
        await store.SaveProfileAsync(new("Renamed", "Private", null, true));
        var overview = await new LibraryStore(store.DefaultRoot).ReadProfileOverviewAsync();
        Require(overview.Identity == first.Identity, "Profile edits regenerated identity");
        Require(overview.Statistics.Stickers == 2 && overview.Statistics.StillImages == 1 && overview.Statistics.AnimatedImages == 1, "Sticker membership inflated profile counts");
        Require(overview.Statistics.Packs == 2 && overview.Statistics.PublicPacks == 1 && overview.Statistics.PrivatePacks == 1 && overview.Statistics.Favorites == 1, "Profile pack or favourite counts are incorrect");
        Require(overview.Statistics.StorageBytes > 0, "Profile storage is missing");
        await store.ChangeFolderAsync(store.Root + "-identity-moved");
        Require((await new LibraryStore(store.DefaultRoot).ReadProfileOverviewAsync()).Identity == first.Identity, "Relocation regenerated identity");
    });
    await Check("pack updates count committed changes once, exclude no-ops, and survive reopening", async store =>
    {
        var packId = await store.CreatePackAsync("Tracked", "Publisher");
        Require((await store.ReadAsync()).Packs.Single().UpdateCount == 0, "Creating a pack is not an update");
        var stickerId = await store.ImportAsync("Original.png", png);
        await store.AddToPackAsync(packId, [stickerId]);
        await store.AddToPackAsync(packId, [stickerId]);
        Require((await store.ReadAsync()).Packs.Single().UpdateCount == 1, "No-op membership counted as an update");
        var source = Path.Combine(store.DefaultRoot, "batch.png");
        await File.WriteAllBytesAsync(source, png);
        await store.ImportBatchAsync([new("Batch A", source), new("Batch B", source)], packId);
        Require((await store.ReadAsync()).Packs.Single().UpdateCount == 2, "A multi-file batch must count once");
        await store.UpdatePackDetailsAsync(packId, "Renamed", "Publisher");
        await store.UpdatePackDetailsAsync(packId, "Renamed", "Publisher");
        await store.SetPackCoverAsync(packId, png);
        await store.SetPackCoverAsync(packId, png);
        await store.SetPackPublicAsync(packId, true);
        await store.SetPackPublicAsync(packId, true);
        await store.EditStickerAsync(stickerId, "Edited sticker", "new tags");
        await store.FavoriteAsync(stickerId);
        await Throws(() => store.UpdatePackDetailsAsync(packId, "Should fail", ""));
        var overview = await new LibraryStore(store.DefaultRoot).ReadProfileOverviewAsync();
        var activity = overview.PackActivity.Single();
        Require(activity.Updates == 6 && overview.Statistics.PackUpdates == 6 && activity.LastUpdatedUtc is not null, "Update totals included no-ops, favourites, or failed writes, or did not persist");
        await store.ChangeFolderAsync(store.Root + "-activity-moved");
        Require((await new LibraryStore(store.DefaultRoot).ReadProfileOverviewAsync()).PackActivity.Single().Updates == 6, "Relocation lost update counts");
    });
    Console.WriteLine($"Passed {checks} storage checks.");
}
finally
{
    // The unique directory was created by this test run and contains only test fixtures.
    if (Path.GetDirectoryName(root) == Path.TrimEndingDirectorySeparator(Path.GetTempPath()) &&
        Path.GetFileName(root).StartsWith("stick-it-storage-checks-", StringComparison.Ordinal))
        Directory.Delete(root, true);
}

async Task Check(string name, Func<LibraryStore, Task> action)
{
    var store = new LibraryStore(Path.Combine(root, (++checks).ToString()));
    await action(store);
    Console.WriteLine($"PASS {name}");
}

static void Require(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}

static async Task Throws(Func<Task> action)
{
    try { await action(); }
    catch (InvalidOperationException) { return; }
    throw new Exception("Expected operation to reject the unsafe or over-budget change");
}

static async Task RejectDownload(Func<Task<byte[]>> operation)
{
    try { await operation(); } catch (Exception error) when (error is InvalidDataException or HttpRequestException) { return; }
    throw new Exception("Expected invalid GIF download to fail");
}

sealed class FixtureHttpHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => respond(request, cancellationToken);
}

sealed class FixtureProgress<T>(Action<T> report) : IProgress<T>
{
    public void Report(T value) => report(value);
}
