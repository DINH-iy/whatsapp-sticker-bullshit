using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using StickerApp.Core;

internal static class EditableBackupChecks
{
    public static async Task<int> RunAsync(string root, byte[] png, byte[] webp, byte[] animation)
    {
        var source = new LibraryStore(Path.Combine(root, "layer-backup-source"));
        var font = await source.ImportFontAsync("Caption.ttf", await File.ReadAllBytesAsync("StickerApp.Shared/wwwroot/fonts/Anton-Regular.ttf"));
        var composition = new LayerComposition { Layers = [
            new() { Id="art", Name="Animated artwork", Source="data:image/png;base64,"+Convert.ToBase64String(png),
                GifSource="data:image/webp;base64,"+Convert.ToBase64String(animation), GifDuration=1, TrimStart=.2, TrimEnd=.8,
                X=180, Y=220, Width=260, Height=190, Rotation=35, FlipX=true, Opacity=.6, Hue=45, ColorStrength=.4, BorderWidth=5, BorderColor="#44ee77" },
            new() { Id="text", Name="Caption", Source="data:image/png;base64,"+Convert.ToBase64String(png),
                Text=new() { Content="TOP TEXT 😎", Font=font.Id, FontData=font.Data, Gradient=true, Bold=true, Underline=true, Shadow=true } }
        ] };
        var pack = await source.CreatePackAsync("Portable layers", "Backup checks", isPublic:true);
        var layered = await source.SaveCompositionAsync(null, "Animation with caption", animation, composition, pack, nsfw:true);
        var still = await source.SaveCompositionAsync(null, "Still layers", webp, new() { Layers=[new(){Id="still",Name="Artwork",Source=composition.Layers[1].Source}] },pack);
        var plain = await source.ImportAsync("Plain.png",png);
        await source.AddToPackAsync(pack,[plain]);
        using var backup = new MemoryStream();
        var exported = await source.ExportPackAsync(pack,backup);
        using var again = new MemoryStream();
        var repeated = await source.ExportPackAsync(pack,again);
        Require(exported.Sha256==repeated.Sha256 && backup.ToArray().SequenceEqual(again.ToArray()),"Unchanged backups must have stable bytes and checksums");
        using(var zip=new ZipArchive(backup,ZipArchiveMode.Read,true))
        {
            Require(zip.Entries.Count==6,"Mixed backup must list exactly three media files, two compositions and a manifest");
            var manifest=JsonSerializer.Deserialize<PackArchive.Manifest>(await Read(zip.GetEntry("manifest.json")!))!;
            Require(manifest.Version==2 && manifest.Images[0].CompositionFile=="layers/00.layers.json" && manifest.Images[2].CompositionFile is null,"Layer metadata/version or mixed pack order was lost");
        }
        var target = new LibraryStore(Path.Combine(root,"layer-backup-target"));
        backup.Position=0;
        var importedId=await target.ImportPackAsync(backup);
        var library=await target.ReadAsync();
        var importedPack=library.Packs.Single();
        Require(importedPack.Id==importedId && importedPack.Public && importedPack.Publisher=="Backup checks","Pack metadata was lost");
        var restored=library.Stickers.Single(s=>s.Id==importedPack.StickerIds[0]);
        Require(restored.Id!=layered && restored.Nsfw && restored.Animated,"Fresh identity, spoiler or animation metadata was lost");
        var reopened=await new LibraryStore(target.DefaultRoot).ReadCompositionAsync(restored.Id);
        Require(JsonSerializer.Serialize(reopened)==JsonSerializer.Serialize(composition),"Source media, text/font, order, trim or layer formatting changed during transfer");
        Require((await target.ReadFontsAsync()).Count==0,"Transfer must not require installing the embedded caption font in the target library");
        Require(await target.ReadCompositionAsync(importedPack.StickerIds[1]) is not null && await target.ReadCompositionAsync(importedPack.StickerIds[2]) is null,"Mixed still/layered status was lost");
        reopened!.Layers[1].Text!.Content="EDITED AFTER IMPORT 👴";
        reopened.Layers[0].X=300;
        await target.SaveCompositionAsync(restored.Id,restored.Name,animation,reopened);
        var edited=await new LibraryStore(target.DefaultRoot).ReadCompositionAsync(restored.Id);
        Require(edited!.Layers[1].Text!.Content=="EDITED AFTER IMPORT 👴" && edited.Layers[0].X==300,"Restored composition could not be edited and saved again");
        Console.WriteLine("PASS editable ZIP transfers still/animated layers, embedded fonts, text, trims, effects, order and fresh editable identities");

        var original=backup.ToArray();
        var before=await File.ReadAllBytesAsync(Path.Combine(target.Root,"library.json"));
        var files=Directory.GetFiles(Path.Combine(target.Root,"media")).Order().ToArray();
        foreach(var fault in new[]{"checksum","missing","path","version","invalid-artwork","unlisted","duplicate","oversized"})
        {
            using var broken=await Corrupt(original,fault);
            var rejected=false;
            try {await target.ImportPackAsync(broken);} catch(InvalidDataException){rejected=true;}
            Require(rejected,"Accepted damaged layer backup: "+fault);
            var currentIndex=await File.ReadAllBytesAsync(Path.Combine(target.Root,"library.json"));
            Require(before.SequenceEqual(currentIndex) && files.SequenceEqual(Directory.GetFiles(Path.Combine(target.Root,"media")).Order()),"Rejected backup modified the target library: "+fault);
        }
        var limited=new LibraryStore(Path.Combine(root,"layer-backup-limited"));
        await limited.SetStorageLimitAsync(1024*1024);
        // A valid composition can repeat an embedded font on several independent text layers.
        var big=new LayerComposition {Layers=Enumerable.Range(0,10).Select(i=>new CompositionLayer {Id=i.ToString(),Name="Caption",Source=composition.Layers[1].Source,Text=composition.Layers[1].Text}).ToList()};
        var largePack=await source.CreatePackAsync("Larger source");
        await source.SaveCompositionAsync(null,"Ten captions",webp,big,largePack);
        using var largeBackup=new MemoryStream(); await source.ExportPackAsync(largePack,largeBackup);
        var overBudget=false;try {await limited.ImportPackAsync(largeBackup);}catch(InvalidOperationException){overBudget=true;}
        Require(overBudget && (await limited.ReadAsync()).Stickers.Count==0 && Directory.GetFiles(Path.Combine(limited.Root,"media")).Length==0,"Storage limit must include compositions and roll back all staged files");
        Console.WriteLine("PASS damaged/missing/unlisted layers, checksums, unsafe paths and storage limits reject atomically");

        var thirtyPack=await source.CreatePackAsync("Thirty layered stickers");
        for(var i=0;i<30;i++)await source.SaveCompositionAsync(null,"Layer "+i,webp,new(){Layers=[new(){Id="art",Name="Art",Source=composition.Layers[1].Source}]},thirtyPack);
        using var full=new MemoryStream(); await source.ExportPackAsync(thirtyPack,full);
        var fullRead=await PackArchive.ReadAsync(full);
        Require(fullRead.Images.Count==30 && fullRead.Images.All(i=>i.CompositionBytes is not null),"The full 61-entry layered archive was rejected");
        var oldPack=await source.CreatePackAsync("Plain legacy-compatible backup");await source.AddToPackAsync(oldPack,[plain]);
        using var legacy=new MemoryStream();await source.ExportPackAsync(oldPack,legacy);
        using(var zip=new ZipArchive(legacy,ZipArchiveMode.Read,true))Require(JsonSerializer.Deserialize<PackArchive.Manifest>(await Read(zip.GetEntry("manifest.json")!))!.Version==1,"Plain backups must stay compatible with version 1");
        legacy.Position=0;Require((await PackArchive.ReadAsync(legacy)).Images.Single().CompositionBytes is null,"Version 1 import regressed");
        var whatsappPack=await source.CreatePackAsync("Rendered WhatsApp pack");await source.AddToPackAsync(whatsappPack,[layered,still]);
        var cover=png.ToArray();System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(cover.AsSpan(16),96);System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(cover.AsSpan(20),96);
        using var whatsapp=new MemoryStream();await source.ExportPackAsync(whatsappPack,whatsapp,cover);
        var flat=await PackArchive.ReadAsync(whatsapp);
        Require(flat.IsWhatsAppFormat && flat.Images.Count==2 && flat.Images.All(i=>i.CompositionBytes is null),"WhatsApp export must retain its rendered sticker format");
        Console.WriteLine("PASS maximum layered pack, legacy version 1 compatibility and flattened WhatsApp export");
        var groupDocument = new LayerComposition { Version=2, Layers=[new() { Id="group",Name="Group",Source="",Width=512,Height=512,
            Children=JsonSerializer.Deserialize<LayerComposition>(JsonSerializer.Serialize(composition))!.Layers }] };
        var groupPack=await source.CreatePackAsync("Group backup","Checks");
        await source.SaveCompositionAsync(null,"Grouped caption and animation",animation,groupDocument,groupPack);
        using var groupedBackup=new MemoryStream();await source.ExportPackAsync(groupPack,groupedBackup);groupedBackup.Position=0;
        var groupedTarget=new LibraryStore(Path.Combine(root,"group-backup-target"));await groupedTarget.ImportPackAsync(groupedBackup);
        var groupedSticker=(await groupedTarget.ReadAsync()).Stickers.Single();
        var groupedReopened=await groupedTarget.ReadCompositionAsync(groupedSticker.Id);
        Require(JsonSerializer.Serialize(groupedReopened)==JsonSerializer.Serialize(groupDocument),"Group members, text/font and GIF sources were lost in backup transfer");
        var browseSnapshot=await groupedTarget.ReadBrowseSnapshotAsync();
        Require(browseSnapshot.LayerDetails![groupedSticker.Id] is {LayerCount:1,GroupCount:1,HasText:true},"Browse metadata must count the group as one layer and detect editable text");
        groupDocument.Layers[0].Children![0].Children=[new(){Id="nested",Name="Invalid",Source=""}];
        var rejectedGroup=false;try{groupDocument.Validate();}catch(InvalidDataException){rejectedGroup=true;}
        Require(rejectedGroup,"Nested or invalid group members must be rejected");
        Console.WriteLine("PASS grouped layers preserve editable image/text/font/GIF members through pack backup transfer and reject invalid nesting");
        return 4;
    }

    private static async Task<MemoryStream> Corrupt(byte[] bytes,string fault)
    {
        using var input=new MemoryStream(bytes);using var zip=new ZipArchive(input,ZipArchiveMode.Read);
        var entries=new Dictionary<string,byte[]>();foreach(var entry in zip.Entries)entries.Add(entry.FullName,await Read(entry));
        var manifest=JsonSerializer.Deserialize<PackArchive.Manifest>(entries["manifest.json"])!;
        const string layer="layers/00.layers.json";
        switch(fault)
        {
            case "checksum":entries[layer][0]=(byte)'!';break;
            case "missing":entries.Remove(layer);break;
            case "path":manifest.Images[0].CompositionFile="../escaped.layers.json";break;
            case "version":manifest.Version=1;break;
            case "invalid-artwork":entries[layer]="{\"Version\":1,\"Layers\":[]}"u8.ToArray();manifest.Images[0].CompositionSha256=Convert.ToHexString(SHA256.HashData(entries[layer]));break;
            case "oversized":entries[layer]=new byte[LayerComposition.MaxFileBytes+1];break;
            case "unlisted":entries.Add("layers/unlisted.json","{}"u8.ToArray());break;
        }
        entries["manifest.json"]=JsonSerializer.SerializeToUtf8Bytes(manifest);
        var result=new MemoryStream();using(var output=new ZipArchive(result,ZipArchiveMode.Create,true))
        {
            foreach(var entry in entries){await using var stream=output.CreateEntry(entry.Key).Open();await stream.WriteAsync(entry.Value);}
            if(fault=="duplicate"){await using var stream=output.CreateEntry(layer).Open();await stream.WriteAsync(entries[layer]);}
        }
        result.Position=0;return result;
    }
    private static async Task<byte[]> Read(ZipArchiveEntry entry){await using var input=entry.Open();using var output=new MemoryStream();await input.CopyToAsync(output);return output.ToArray();}
    private static void Require(bool value,string message){if(!value)throw new Exception(message);}
}
