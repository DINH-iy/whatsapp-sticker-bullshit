using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace StickerApp.ShareServer.Friends;

public sealed partial class FriendsRepository
{
    public sealed record Invitation(Guid Id,Guid CreatedBy,string Label,string Hash,DateTimeOffset ExpiresAt,int RemainingUses);
    public sealed record InvitationInfo(Guid Id,string Label,string CreatedBy,DateTimeOffset ExpiresAt,int RemainingUses,bool CanRevoke);
    public sealed record NewInvitation(Guid Id,string Label,string Code,DateTimeOffset ExpiresAt,int RemainingUses);
    public sealed record InvitationPreview(string GroupName,DateTimeOffset? ExpiresAt,int? RemainingUses,bool OwnerSetup);
    public sealed partial class State {public List<Invitation> Invitations{get;set;}=[];}
    public string GroupName{get;private set;}="Friends’ group";
    public async Task<NewInvitation> CreateInvitation(Member member,string? label,int hours,int uses)
    {
        label=(label??"").Trim().ToLowerInvariant();
        if(label.Length>24||(label.Length>0&&!Regex.IsMatch(label,"^[a-z0-9]+(?:-[a-z0-9]+)*$")))throw new InvalidDataException("Use up to 24 lowercase letters, numbers and hyphens for the custom name.");
        if(hours is <1 or >168||uses is <1 or >10)throw new InvalidDataException("Invites can last 1–168 hours and allow 1–10 joins.");
        await gate.WaitAsync();
        try
        {
            var state=await Read();if(!state.Members.Any(value=>value.Id==member.Id&&value.TokenHash==member.TokenHash))throw new UnauthorizedAccessException();
            state.Invitations.RemoveAll(invite=>invite.ExpiresAt<=DateTimeOffset.UtcNow||invite.RemainingUses==0);
            if(state.Invitations.Count(invite=>invite.CreatedBy==member.Id)>=10)throw new InvalidDataException("You have 10 active invites. Revoke one before creating another.");
            if(state.Invitations.Count>=50)throw new InvalidDataException("This group has 50 active invites. Revoke an unused invite first.");
            // A custom name is not the secret: retain an unpredictable suffix.
            var code=label.Length==0?Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant():label+"-"+Convert.ToHexString(RandomNumberGenerator.GetBytes(8)).ToLowerInvariant();
            var invite=new Invitation(Guid.NewGuid(),member.Id,label.Length==0?"Friends invitation":label,Convert.ToHexString(Hash(code)),DateTimeOffset.UtcNow.AddHours(hours),uses);
            state.Invitations.Add(invite);await Save(state);return new(invite.Id,invite.Label,code,invite.ExpiresAt,invite.RemainingUses);
        }
        finally{gate.Release();}
    }
    public async Task<List<InvitationInfo>> Invitations(Member member)
    {
        await gate.WaitAsync();
        try{return(await Read()).Invitations.Where(invite=>invite.CreatedBy==member.Id&&invite.ExpiresAt>DateTimeOffset.UtcNow&&invite.RemainingUses>0).Select(invite=>new InvitationInfo(invite.Id,invite.Label,invite.CreatedBy==member.Id?member.Username:"Group member",invite.ExpiresAt,invite.RemainingUses,invite.CreatedBy==member.Id)).ToList();}
        finally{gate.Release();}
    }
    public async Task<bool> RevokeInvitation(Member member,Guid id)
    {
        await gate.WaitAsync();
        try{var state=await Read();var removed=state.Invitations.RemoveAll(invite=>invite.Id==id&&invite.CreatedBy==member.Id)>0;if(removed)await Save(state);return removed;}
        finally{gate.Release();}
    }
    public async Task<InvitationPreview?> PreviewInvitation(string code)
    {
        if(!Enabled||code.Length>512)return null;
        if(CryptographicOperations.FixedTimeEquals(Hash(code),inviteHash!))return new(GroupName,null,null,true);
        await gate.WaitAsync();
        try{var hash=Hash(code.Trim().ToLowerInvariant());var invite=(await Read()).Invitations.FirstOrDefault(invite=>invite.ExpiresAt>DateTimeOffset.UtcNow&&invite.RemainingUses>0&&CryptographicOperations.FixedTimeEquals(Convert.FromHexString(invite.Hash),hash));return invite is null?null:new(GroupName,invite.ExpiresAt,invite.RemainingUses,false);}
        finally{gate.Release();}
    }
}
