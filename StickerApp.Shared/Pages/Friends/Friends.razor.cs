using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using StickerApp.Shared.Services;

namespace StickerApp.Shared.Pages.Friends;

public partial class Friends
{
    private bool connected, ready, busy;
    private string username="",serverUrl="https://stickers.dinnyvanhuizen.com",progress="",error="",notice="";
    private int offset,total;
    private ElementReference serverInput,inviteInput,inviteLinkOutput,inviteCodeOutput;
    private JoinPreview? joinPreview;
    private string inviteLabel="";
    private int inviteHours=24,inviteUses=1;
    private bool inviteCreated, pendingNoticeShown;
    private List<InviteInfo> activeInvites=[];
    public sealed record JoinPreview(string GroupName,DateTimeOffset? ExpiresAt,int? RemainingUses,bool OwnerSetup,string ServerUrl);
    public sealed record InviteInfo(Guid Id,string Label,DateTimeOffset ExpiresAt,int RemainingUses);
    private IJSObjectReference? module;
    private List<PackCard> cards=[];
    private List<GroupCollectionCard> groupCollections=[];
    private string collectionName="";
    private readonly Dictionary<string,string> previews=[];
    private readonly HashSet<string> revealed=[];
    public sealed record Connection(bool Connected,string ServerUrl,string Username);
    public sealed record PackCard(Guid OwnerId,Guid PackId,string Username,string Name,string Publisher,int Count,bool Animated,bool Nsfw,string? Cover,long Bytes,DateTimeOffset UpdatedAt);
    public sealed record Overview(List<PackCard> Items,int Total,Guid MemberId,string Username);
    protected override void OnInitialized()=>StickerApp.Core.GroupInviteInbox.Changed+=NativeInviteReady;
    private void NativeInviteReady()=>_ = InvokeAsync(StateHasChanged);
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if(StickerApp.Core.FriendsFeature.UnderMaintenance)return;
        if(!firstRender)
        {
            if(ready && !connected)
            {
                var applied=await module!.InvokeAsync<bool>("applyPending",serverInput,inviteInput);
                if(StickerApp.Core.GroupInviteInbox.Take() is { } native){await module!.InvokeVoidAsync("applyInvitation",serverInput,inviteInput,native.ServerUrl,native.Code);applied=true;}
                if(applied){notice="Invitation loaded. Review it before joining.";StateHasChanged();}
            }
            else if(connected && !pendingNoticeShown && StickerApp.Core.GroupInviteInbox.Peek() is not null)
            {pendingNoticeShown=true;notice="You’re already connected to a group. Leave it first if you want to use another invitation.";StateHasChanged();}
            return;
        }
        module=await JS.InvokeAsync<IJSObjectReference>("import","./_content/StickerApp.Shared/js/ui/friends.js");
        var connection=await module!.InvokeAsync<Connection>("connection");connected=connection.Connected;serverUrl=string.IsNullOrEmpty(connection.ServerUrl)?serverUrl:connection.ServerUrl;username=connection.Username;ready=true;
        if(connected)
        {
            await Load();
            if(StickerApp.Core.GroupInviteInbox.Peek() is not null || await module!.InvokeAsync<bool>("hasPendingInvitation"))
            {pendingNoticeShown=true;notice="You’re already connected to a group. Leave it first if you want to use another invitation.";}
        }
        else
        {
            await module!.InvokeAsync<bool>("applyPending",serverInput,inviteInput);
            if(StickerApp.Core.GroupInviteInbox.Take() is { } native)await module!.InvokeVoidAsync("applyInvitation",serverInput,inviteInput,native.ServerUrl,native.Code);
        }
        StateHasChanged();
    }
    private async Task Run(Func<Task> action)
    {
        busy=true;error=notice="";StateHasChanged();
        try {await action();}
        catch(Exception exception) {Log.Error("Friends packs",exception);error=exception is JSException ? exception.Message.Split('\n')[0] : exception.Message;}
        finally {busy=false;StateHasChanged();}
    }
    private Task Join()=>Run(async()=>
    {
        var profile=await Store.ReadProfileAsync();if(string.IsNullOrWhiteSpace(profile.Username))throw new InvalidOperationException("Add a username in your profile before joining.");
        var result=await module!.InvokeAsync<Connection>("joinReviewed",profile.Username);connected=result.Connected;username=result.Username;serverUrl=result.ServerUrl;await Refresh();
    });
    private Task PreviewInvite()=>Run(async()=>{progress="Checking invitation…";joinPreview=await module!.InvokeAsync<JoinPreview>("previewJoin",serverInput,inviteInput);});
    private Task MakeInvitation()=>Run(async()=>{progress="Creating invitation…";await module!.InvokeAsync<InviteInfo>("createInvitation",inviteLabel,inviteHours,inviteUses,inviteLinkOutput,inviteCodeOutput);inviteCreated=true;activeInvites=await module!.InvokeAsync<List<InviteInfo>>("invitations");});
    private Task CopyInvitation()=>Run(async()=>{notice=await module!.InvokeAsync<string>("copyInvitation",inviteLinkOutput);});
    private Task ShareInvitation()=>Run(async()=>{notice=await module!.InvokeAsync<string>("shareInvitation",inviteLinkOutput);});
    private Task RevokeInvite(Guid id)=>Run(async()=>{await module!.InvokeVoidAsync("revokeInvitation",id.ToString());activeInvites=await module!.InvokeAsync<List<InviteInfo>>("invitations");inviteCreated=false;});
    private Task Load()=>Run(Refresh);
    private async Task Refresh()
    {
        progress="Loading friends’ packs…";await module!.InvokeVoidAsync("clearPreviews");previews.Clear();
        activeInvites=await module!.InvokeAsync<List<InviteInfo>>("invitations");
        groupCollections=await module!.InvokeAsync<List<GroupCollectionCard>>("collections");
        var overview=await module!.InvokeAsync<Overview>("list",offset);cards=overview.Items;total=overview.Total;
        foreach(var pack in cards)
        {
            try {previews[Key(pack)]=await module!.InvokeAsync<string>("preview",pack.OwnerId.ToString(),pack.PackId.ToString());}
            catch(JSException) { /* A missing preview does not block names or pack downloads. */ }
        }
    }
    private Task CreateCollection()=>Run(async()=>{progress="Creating group collection…";var created=await module!.InvokeAsync<GroupCollectionCard>("createCollection",collectionName);collectionName="";await Refresh();notice=$"Created {created.Name}. Open it to add stickers.";});
    private Task Sync()=>Run(async()=> {progress="Syncing public packs…";var count=await new FriendsPackService(Store,JS).SyncPublicPacksAsync(value=>{progress=value;_ = InvokeAsync(StateHasChanged);});offset=0;await Refresh();notice=$"Synced {count} public packs.";});
    private Task Import(PackCard pack)=>Run(async()=>
    {
        progress=$"Importing {pack.Name}…";
        await using var reference=await module!.InvokeAsync<IJSStreamReference>("download",pack.OwnerId.ToString(),pack.PackId.ToString());
        await using var stream=await reference.OpenReadStreamAsync(StickerApp.Core.PackArchive.MaxArchiveBytes);
        await Store.ImportPackAsync(stream, forcePrivate: true);notice=$"Imported {pack.Name} into My Packs as a private copy, including available editable layers.";
    });
    private Task Disconnect()=>Run(async()=>{await module!.InvokeVoidAsync("disconnect");connected=false;cards.Clear();previews.Clear();});
    private static string Key(PackCard pack) => pack.OwnerId.ToString("N") + pack.PackId.ToString("N");
    private void Reveal(string id){if(!revealed.Remove(id))revealed.Add(id);}
    private async Task Next(){offset+=12;await Load();}
    private async Task Previous(){offset=Math.Max(0,offset-12);await Load();}
    public async ValueTask DisposeAsync(){StickerApp.Core.GroupInviteInbox.Changed-=NativeInviteReady;if(module is not null)try{await module!.InvokeVoidAsync("clearPreviews");await module.DisposeAsync();}catch(JSDisconnectedException){}}
}
