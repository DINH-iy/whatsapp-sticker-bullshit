using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using StickerApp.Core;

internal static class CollectionChecks
{
    private sealed record Card(Guid Id,string Name,int Revision,int Count);
    private sealed record Vote(string Emoji,int Count,bool Mine);
    private sealed record Sticker(Guid Id,string Name,string AddedBy,string EditedBy,List<Vote> Reactions);
    private sealed record Detail(Card Collection,List<Sticker> Stickers,bool CanDelete);
    public static async Task Run(HttpClient anonymous,HttpClient alice,HttpClient bob,LibraryStore source,Guid pack,LayerComposition composition,byte[] rendered)
    {
        var created=await alice.PostAsJsonAsync("/api/friends/collections",new{name="Shared fixture"});created.EnsureSuccessStatusCode();
        var card=(await created.Content.ReadFromJsonAsync<Card>())!;var path=$"/api/friends/collections/{card.Id}";
        Require((await anonymous.GetAsync(path)).StatusCode==HttpStatusCode.Unauthorized,"Anonymous collection access was allowed");
        Require(card.Revision==1&&card.Count==0,"New collection must be empty at revision 1");
        var bytes=await Export(source,pack);
        var added=await Put(bob,path,1,bytes);added.EnsureSuccessStatusCode();
        var detail=(await alice.GetFromJsonAsync<Detail>(path))!;
        Require(detail.Collection.Revision==2&&detail.Stickers.Single().AddedBy=="Bob fixture"&&detail.Stickers[0].EditedBy=="Bob fixture","Adding a sticker did not attribute it to the authenticated member");
        var sticker=detail.Stickers[0].Id;
        composition.Layers[0].Text!.Content="ALICE EDITED 😎";
        var local=(await source.ReadAsync()).Stickers.Single();await source.SaveCompositionAsync(local.Id,local.Name,rendered,composition);
        var editedBytes=await Export(source,pack);var edited=await Put(alice,path,2,editedBytes);edited.EnsureSuccessStatusCode();
        detail=(await bob.GetFromJsonAsync<Detail>(path))!;
        Require(detail.Stickers[0].AddedBy=="Bob fixture"&&detail.Stickers[0].EditedBy=="Alice fixture","Editing a sticker changed its creator or lost editor attribution");
        Require((await Put(bob,path,2,bytes)).StatusCode==HttpStatusCode.Conflict,"A stale edit silently overwrote newer work");
        Require((await bob.GetAsync(path+"/download?revision=2")).StatusCode==HttpStatusCode.Conflict,"Stale download revision was accepted");
        var download=await bob.GetByteArrayAsync(path+"/download?revision=3");
        using(var input=new MemoryStream(download)){var imported=await PackArchive.ReadAsync(input);var document=JsonSerializer.Deserialize<LayerComposition>(imported.Images.Single().CompositionBytes!)!;Require(document.Layers[0].Text!.Content=="ALICE EDITED 😎"&&imported.Images[0].SourceId==sticker,"Conflict rejection changed content or sticker identities");}
        Require((await anonymous.GetAsync(path+$"/stickers/{sticker}/preview")).StatusCode==HttpStatusCode.Unauthorized,"Collection preview bypassed membership");
        var reactionPath=path+$"/stickers/{sticker}/reaction";
        (await bob.PostAsJsonAsync(reactionPath,new{emoji="🔥"})).EnsureSuccessStatusCode();
        detail=(await bob.GetFromJsonAsync<Detail>(path))!;Require(detail.Stickers[0].Reactions.Single().Mine&&detail.Stickers[0].Reactions[0].Count==1,"Reaction is not attributed to its member");
        (await alice.PostAsJsonAsync(reactionPath,new{emoji="🔥"})).EnsureSuccessStatusCode();
        detail=(await alice.GetFromJsonAsync<Detail>(path))!;Require(detail.Stickers[0].Reactions.Single().Count==2,"Reaction counts were not combined");
        (await bob.PostAsJsonAsync(reactionPath,new{emoji="❤️"})).EnsureSuccessStatusCode();
        (await bob.PostAsJsonAsync(reactionPath,new{emoji="🦦"})).EnsureSuccessStatusCode();
        Require((await bob.PostAsJsonAsync(reactionPath,new{emoji="<script>"})).StatusCode==HttpStatusCode.BadRequest,"Invalid reaction was accepted");
        (await Put(bob,path,3,editedBytes)).EnsureSuccessStatusCode();detail=(await alice.GetFromJsonAsync<Detail>(path))!;
        Require(detail.Collection.Revision==4&&detail.Stickers[0].EditedBy=="Alice fixture"&&detail.Stickers[0].Reactions.Count==2,"An unchanged sticker edit lost reactions or incorrectly changed its editor");
        Require((await bob.DeleteAsync(path)).StatusCode==HttpStatusCode.Forbidden,"A member deleted another creator's collection");
        (await alice.DeleteAsync(path)).EnsureSuccessStatusCode();Require((await bob.GetAsync(path)).StatusCode==HttpStatusCode.NotFound,"Deleted collection remained visible");
        Require((await bob.GetAsync(path+"/download?revision=4")).StatusCode==HttpStatusCode.NotFound,"Deleted collection remained downloadable");
        Console.WriteLine("PASS collaborative add/edit attribution, stable sticker identities, revision conflicts, reactions, creator-only deletion and protected previews/downloads");
    }
    private static async Task<byte[]> Export(LibraryStore source,Guid pack){using var stream=new MemoryStream();await source.ExportPackAsync(pack,stream);return stream.ToArray();}
    private static async Task<HttpResponseMessage> Put(HttpClient client,string path,int revision,byte[] bytes){using var request=new HttpRequestMessage(HttpMethod.Put,path){Content=new ByteArrayContent(bytes)};request.Headers.TryAddWithoutValidation("If-Match",$"\"{revision}\"");return await client.SendAsync(request);}
    private static void Require(bool value,string message){if(!value)throw new Exception(message);}
}
