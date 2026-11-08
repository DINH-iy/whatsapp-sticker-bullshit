using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using StickerApp.Core;

namespace StickerApp.Shared.Pages.Friends;

public partial class Collection
{
    [Parameter] public Guid CollectionId { get; set; }
    private IJSObjectReference? module;
    private GroupCollectionDetail? detail;
    private bool busy,confirmDelete;
    private Guid? reactionMenu,customFor;
    private string customEmoji="";
    private string error="",progress="";
    private Dictionary<Guid,string> previews=[];
    private readonly HashSet<Guid> revealed=[];
    private static readonly string[] emojiChoices=["🔥","❤️","😂","👍","😮","💀"];
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if(!firstRender)return;module=await JS.InvokeAsync<IJSObjectReference>("import","./_content/StickerApp.Shared/js/ui/friends.js");await Refresh();
    }
    protected override Task OnParametersSetAsync()=>module is null?Task.CompletedTask:Refresh();
    private async Task Run(Func<Task> action)
    {
        busy=true;error="";StateHasChanged();
        try {await action();}catch(Exception exception){error=exception.Message.Split('\n')[0];}finally{busy=false;StateHasChanged();}
    }
    private Task Refresh()=>Run(async()=>
    {
        progress="Loading collection…";detail=await module!.InvokeAsync<GroupCollectionDetail>("collection",CollectionId.ToString());
        await module!.InvokeVoidAsync("clearPreviews");previews.Clear();
        foreach(var sticker in detail.Stickers)try{previews[sticker.Id]=await module!.InvokeAsync<string>("collectionPreview",CollectionId.ToString(),sticker.Id.ToString());}catch(JSException){}
    });
    private Task Edit()=>Run(async()=>
    {
        if(detail is null)return;progress="Opening a private working copy…";
        var connection=await module!.InvokeAsync<Friends.Connection>("connection");
        await using var reference=await module!.InvokeAsync<IJSStreamReference>("collectionDownload",CollectionId.ToString(),detail.Collection.Revision);
        await using var stream=await reference.OpenReadStreamAsync(PackArchive.MaxArchiveBytes);
        var id=await Store.ImportPackAsync(stream,forcePrivate:true,preserveSourceIds:true);
        await Store.LinkCollectionAsync(id,new(connection.ServerUrl,CollectionId,detail.Collection.Revision));
        Navigation.NavigateTo($"packs/{id}");
    });
    private Task React(Guid sticker,string emoji)=>Run(async()=>
    {
        reactionMenu=null;customFor=null;customEmoji="";progress="Saving reaction…";await module!.InvokeVoidAsync("react",CollectionId.ToString(),sticker.ToString(),emoji);
        detail=await module!.InvokeAsync<GroupCollectionDetail>("collection",CollectionId.ToString());
    });
    private Task Delete()=>Run(async()=>{await module!.InvokeVoidAsync("removeCollection",CollectionId.ToString());Navigation.NavigateTo("friends");});
    private void Reveal(Guid id){if(!revealed.Remove(id))revealed.Add(id);}
    public async ValueTask DisposeAsync(){if(module is not null)try{await module!.InvokeVoidAsync("clearPreviews");await module.DisposeAsync();}catch(JSDisconnectedException){}}
}
