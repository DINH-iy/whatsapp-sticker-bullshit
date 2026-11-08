namespace StickerApp.Shared.Pages.Friends;

public sealed record GroupCollectionCard(Guid Id,string Name,string CreatedBy,string EditedBy,int Revision,int Count,DateTimeOffset UpdatedAt);
public sealed record GroupReaction(string Emoji,int Count,bool Mine);
public sealed record GroupSticker(Guid Id,string Name,bool Nsfw,string AddedBy,string EditedBy,DateTimeOffset EditedAt,List<GroupReaction> Reactions);
public sealed record GroupCollectionDetail(GroupCollectionCard Collection,List<GroupSticker> Stickers,bool CanDelete);
