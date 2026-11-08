using System.Text.Json;
using Microsoft.Extensions.Configuration;
using StickerApp.Core;
using StickerApp.ShareServer.Friends;

internal static class RepositoryChecks
{
    public static async Task Run(string root)
    {
        const string setup="repository-fixture-setup-code-000000";
        var folder=Path.Combine(root,"repository");
        var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"Friends:StoragePath",folder},{"Friends:InviteCode",setup},{"Friends:MemberDailyTransferBytes","100"},{"Friends:GroupDailyTransferBytes","150"}}).Build();
        var repo=new FriendsRepository(config);var first=(await repo.Join(setup,"One"))!;var second=(await repo.Join(setup,"Two"))!;
        var a=(await repo.Authenticate(first.Token))!;var b=(await repo.Authenticate(second.Token))!;
        Require(await repo.ReserveTransfer(a,80)&&!await repo.ReserveTransfer(a,30)&&await repo.ReserveTransfer(b,60)&&!await repo.ReserveTransfer(b,20),"Daily device/group transfer limits were not enforced");
        var restored=new FriendsRepository(config);Require(await restored.Authenticate(first.Token) is not null&&!await restored.ReserveTransfer(a,30),"Membership/quota persistence failed");
        var invitation=await repo.CreateInvitation(a,"menno-was-erbij",24,1);
        Require(invitation.Code.StartsWith("menno-was-erbij-")&&invitation.Code.Length==32,"Custom invitation must retain an unpredictable suffix");
        Require(await repo.PreviewInvitation(invitation.Code) is not null,"Valid invitation preview failed");
        Require(await repo.Join(invitation.Code,"Guest") is not null&&await repo.Join(invitation.Code,"Replay") is null,"Single-use invite was replayed");
        var revoked=await repo.CreateInvitation(a,"",24,2);Require(!await repo.RevokeInvitation(b,revoked.Id)&&await repo.RevokeInvitation(a,revoked.Id)&&await repo.Join(revoked.Code,"Revoked") is null,"Invitation ownership or revocation failed");
        var expired=await repo.CreateInvitation(a,"",1,1);
        var index=Path.Combine(folder,"friends.json");var state=JsonSerializer.Deserialize<FriendsRepository.State>(await File.ReadAllTextAsync(index))!;
        var stored=state.Invitations.Single(invite=>invite.Id==expired.Id);state.Invitations.Remove(stored);state.Invitations.Add(stored with{ExpiresAt=DateTimeOffset.UtcNow.AddMinutes(-1)});
        await File.WriteAllTextAsync(index,JsonSerializer.Serialize(state));Require(await repo.Join(expired.Code,"Expired") is null,"Expired invitation was accepted");
        var persisted=await File.ReadAllTextAsync(index);Require(!persisted.Contains(first.Token)&&!persisted.Contains(expired.Code)&&!persisted.Contains(setup),"Raw membership/invitation credentials were persisted");
        var bad=GroupInvitation.Parse("javascript:alert(1)");Require(bad is null,"Unsafe invitation URI was accepted");
        var native=GroupInvitation.Parse("stikerin://join#server=https%3A%2F%2Fexample.com&code="+invitation.Code);
        Require(native?.ServerUrl=="https://example.com"&&native.Code==invitation.Code,"Native invite parsing failed");
        Require(GroupInvitation.Parse("stikerin://join#server=http%3A%2F%2Fexample.com&code="+invitation.Code) is null,"Remote plaintext invitation server was accepted");
        Require(GroupInvitation.Parse("https://user@example.com/invite#code="+invitation.Code) is null,"Embedded URL credentials were accepted");
        Require(GroupInvitation.Parse("https://example.com/invite#code="+invitation.Code+"&code=duplicate") is null,"Duplicate invitation parameters were accepted");
        foreach(var emoji in new[]{"🦦","👨‍👩‍👧‍👦","👍🏽","🇳🇱","1️⃣","❤️"})Require(EmojiReaction.IsValid(emoji),"Valid custom emoji rejected: "+emoji);
        foreach(var text in new[]{"hello","🔥🔥","<script>"," ",""})Require(!EmojiReaction.IsValid(text),"Non-emoji reaction accepted");
        Console.WriteLine("PASS durable transfer quotas, hashed credentials, custom/random invitation codes, one-use/expiry/revocation and safe native URL parsing");
    }
    private static void Require(bool value,string message){if(!value)throw new Exception(message);}
}
