using System.Security.Cryptography;
using System.Text.Json;
using StickerApp.Core;

namespace StickerApp.ShareServer.Friends;

public sealed class CollectionConflictException : Exception
{
    public CollectionConflictException() : base("Someone else updated this collection. Your local edits are safe in My Packs. Open the latest collection and merge your changes before updating it.") { }
}

public sealed partial class FriendsRepository
{
    public sealed class CollectionSticker
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = "";
        public string File { get; set; } = "";
        public string MimeType { get; set; } = "";
        public bool Nsfw { get; set; }
        public Guid CreatedBy { get; set; }
        public Guid EditedBy { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset EditedAt { get; set; }
        public string ContentHash { get; set; } = "";
        // One reaction per member per sticker; choosing another replaces it.
        public Dictionary<Guid,string> Reactions { get; set; } = [];
    }
    public sealed record StoredCollection(Guid Id, string Name, Guid CreatedBy, Guid EditedBy, int Revision,
        string File, long Bytes, DateTimeOffset UpdatedAt, List<CollectionSticker> Stickers);
    public sealed record ReactionCount(string Emoji, int Count, bool Mine);
    public sealed record StickerCredit(Guid Id,string Name,bool Nsfw,string AddedBy,string EditedBy,DateTimeOffset EditedAt,List<ReactionCount> Reactions);
    public sealed record CollectionCard(Guid Id,string Name,string CreatedBy,string EditedBy,int Revision,int Count,DateTimeOffset UpdatedAt);
    public sealed record CollectionDetail(CollectionCard Collection,List<StickerCredit> Stickers,bool CanDelete);
    public sealed partial class State { public List<StoredCollection> Collections { get; set; } = []; }
    public static readonly string[] ReactionEmoji = ["🔥","❤️","😂","👍","😮","💀"];
    private static string MemberName(State state,Guid id) => state.Members.FirstOrDefault(member=>member.Id==id)?.Username ?? "Former member";
    private static CollectionCard Card(State state,StoredCollection collection) => new(collection.Id,collection.Name,MemberName(state,collection.CreatedBy),MemberName(state,collection.EditedBy),collection.Revision,collection.Stickers.Count,collection.UpdatedAt);
    public async Task<List<CollectionCard>> Collections()
    {
        await gate.WaitAsync();
        try {var state=await Read();return state.Collections.OrderByDescending(collection=>collection.UpdatedAt).Select(collection=>Card(state,collection)).ToList();}
        finally {gate.Release();}
    }
    public async Task<CollectionDetail?> Collection(Guid id,Member member)
    {
        await gate.WaitAsync();
        try
        {
            var state=await Read();var collection=state.Collections.FirstOrDefault(collection=>collection.Id==id);if(collection is null)return null;
            return new(Card(state,collection),collection.Stickers.Select(sticker=>new StickerCredit(sticker.Id,sticker.Name,sticker.Nsfw,
                MemberName(state,sticker.CreatedBy),MemberName(state,sticker.EditedBy),sticker.EditedAt,
                sticker.Reactions.GroupBy(reaction=>reaction.Value).Select(group=>new ReactionCount(group.Key,group.Count(),group.Any(reaction=>reaction.Key==member.Id))).ToList())).ToList(), collection.CreatedBy==member.Id || !state.Members.Any(value=>value.Id==collection.CreatedBy));
        }
        finally {gate.Release();}
    }
    public async Task<CollectionCard> CreateCollection(Member member,string name)
    {
        name=name.Trim();if(name.Length is <1 or >128||name.Any(char.IsControl))throw new InvalidDataException("Choose a collection name of 1–128 characters.");
        var pack=new StickerPack{Name=name,Publisher=member.Username};
        using var stream=new MemoryStream();await PackArchive.WriteAsync(stream,pack,[],_=>throw new InvalidOperationException());
        return await UpdateCollection(member,Guid.NewGuid(),0,stream,default);
    }
    public async Task<CollectionCard> UpdateCollection(Member member,Guid id,int expected,Stream input,CancellationToken cancellation)
    {
        var temporary=Path.Combine(root,Guid.NewGuid().ToString("N")+".upload");string? newPath=null;
        try
        {
            await using(var output=File.Create(temporary))
            {
                var buffer=new byte[65536];long bytes=0;int count;
                while((count=await input.ReadAsync(buffer,cancellation))>0){bytes+=count;if(bytes>PackArchive.MaxArchiveBytes)throw new InvalidDataException("A collection exceeds 100 MB.");await output.WriteAsync(buffer.AsMemory(0,count),cancellation);}
            }
            PackArchive.ImportedPack imported;
            await using(var file=File.OpenRead(temporary))imported=await PackArchive.ReadAsync(file);
            if(imported.IsWhatsAppFormat)throw new InvalidDataException("Update collections with an editable ZIP backup.");
            var ids=new HashSet<Guid>();
            if(imported.Images.Any(image=>image.SourceId is null||!ids.Add(image.SourceId.Value)))throw new InvalidDataException("Collection stickers need unique backup identities. Update the app before editing this collection.");
            await gate.WaitAsync(cancellation);
            try
            {
                var state=await Read();if(!state.Members.Any(value=>value.Id==member.Id&&value.TokenHash==member.TokenHash))throw new UnauthorizedAccessException();
                var old=state.Collections.FirstOrDefault(collection=>collection.Id==id);
                if((old is null&&expected!=0)||(old is not null&&old.Revision!=expected))throw new CollectionConflictException();
                if(old is null&&state.Collections.Count>=20)throw new InvalidDataException("This group supports up to 20 shared collections.");
                var length=new FileInfo(temporary).Length;
                if(state.Packs.Sum(pack=>pack.Bytes)+state.Collections.Sum(collection=>collection.Bytes)-(old?.Bytes??0)+length>limit)throw new SharingStorageFullException();
                var now=DateTimeOffset.UtcNow;var stickers=new List<CollectionSticker>();
                for(var index=0;index<imported.Images.Count;index++)
                {
                    var image=imported.Images[index];var previous=old?.Stickers.FirstOrDefault(sticker=>sticker.Id==image.SourceId);
                    var hash=Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new{image.Name,image.Tags,Nsfw=image.Nsfw||imported.Nsfw,Media=Convert.ToHexString(SHA256.HashData(image.Bytes)),Layers=image.CompositionBytes is null?null:Convert.ToHexString(SHA256.HashData(image.CompositionBytes))})));
                    var changed=previous?.ContentHash!=hash;
                    stickers.Add(new(){Id=image.SourceId!.Value,Name=image.Name,File=$"media/{index:D2}{image.Info.Extension}",MimeType=image.Info.MimeType,Nsfw=image.Nsfw||imported.Nsfw,
                        CreatedBy=previous?.CreatedBy??member.Id,CreatedAt=previous?.CreatedAt??now,EditedBy=changed?member.Id:previous!.EditedBy,EditedAt=changed?now:previous!.EditedAt,
                        ContentHash=hash,Reactions=previous?.Reactions??[]});
                }
                var filename=Guid.NewGuid().ToString("N")+".collection.zip";newPath=Path.Combine(root,filename);File.Move(temporary,newPath);
                var collection=new StoredCollection(id,imported.Name,old?.CreatedBy??member.Id,member.Id,(old?.Revision??0)+1,filename,length,now,stickers);
                state.Collections.RemoveAll(value=>value.Id==id);state.Collections.Add(collection);await Save(state);newPath=null;
                if(old is not null)TryDelete(Path.Combine(root,old.File));return Card(state,collection);
            }
            finally {gate.Release();}
        }
        finally {TryDelete(temporary);if(newPath is not null)TryDelete(newPath);}
    }
    public async Task<bool> React(Member member,Guid collectionId,Guid stickerId,string emoji)
    {
        if(!EmojiReaction.IsValid(emoji))throw new InvalidDataException("Choose a single emoji reaction.");
        await gate.WaitAsync();
        try
        {
            var state=await Read();if(!state.Members.Any(value=>value.Id==member.Id&&value.TokenHash==member.TokenHash))throw new UnauthorizedAccessException();
            var sticker=state.Collections.FirstOrDefault(collection=>collection.Id==collectionId)?.Stickers.FirstOrDefault(sticker=>sticker.Id==stickerId);if(sticker is null)return false;
            if(sticker.Reactions.TryGetValue(member.Id,out var current)&&current==emoji)sticker.Reactions.Remove(member.Id);else sticker.Reactions[member.Id]=emoji;
            await Save(state);return true;
        }
        finally {gate.Release();}
    }
    public async Task<bool> RemoveCollection(Member member,Guid id)
    {
        await gate.WaitAsync();
        try
        {
            var state=await Read();var collection=state.Collections.FirstOrDefault(value=>value.Id==id);if(collection is null)return false;
            if(collection.CreatedBy!=member.Id&&state.Members.Any(value=>value.Id==collection.CreatedBy))throw new UnauthorizedAccessException();
            state.Collections.Remove(collection);await Save(state);TryDelete(Path.Combine(root,collection.File));return true;
        }
        finally{gate.Release();}
    }
    public async Task<(Stream Stream,string Name,CollectionSticker? Sticker)?> OpenCollection(Guid id,int? revision=null,Guid? stickerId=null)
    {
        await gate.WaitAsync();
        try
        {
            var collection=(await Read()).Collections.FirstOrDefault(collection=>collection.Id==id);if(collection is null)return null;
            if(revision.HasValue&&revision.Value!=collection.Revision)throw new CollectionConflictException();
            var sticker=stickerId.HasValue?collection.Stickers.FirstOrDefault(sticker=>sticker.Id==stickerId):null;if(stickerId.HasValue&&sticker is null)return null;
            return(new FileStream(Path.Combine(root,collection.File),FileMode.Open,FileAccess.Read,FileShare.Read|FileShare.Delete),PackArchive.FileNameFor(collection.Name),sticker);
        }
        finally {gate.Release();}
    }
}
