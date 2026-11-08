using System.Buffers.Binary;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using StickerApp.Core;

// Run against the disposable localhost friends server documented in README; never against a user group.
var serverUri=new Uri(args.FirstOrDefault()??"http://127.0.0.1:5204");
if(!serverUri.IsLoopback)throw new InvalidOperationException("Checks require a disposable loopback server.");
using var anonymous = new HttpClient { BaseAddress = serverUri };
const string invitation="local-fixture-invitation-000000000000";
var root=Path.Combine(Path.GetTempPath(),"friends-checks-"+Guid.NewGuid().ToString("N"));
try
{
    var status=await anonymous.GetFromJsonAsync<JsonElement>("/api/friends/status");
    Require(status.GetProperty("enabled").GetBoolean(),"Friends service must be enabled for checks");
    Require((await anonymous.GetAsync("/api/friends/packs")).StatusCode==HttpStatusCode.Unauthorized,"Anonymous users can read the feed");
    var invalid=await anonymous.PostAsJsonAsync("/api/friends/join",new{inviteCode="invalid",username="Fixture"});Require(invalid.StatusCode==HttpStatusCode.Unauthorized,"Invalid invitation was accepted");
    var alice=await Join("Alice fixture");var bob=await Join("Bob fixture");
    using var a=Client(alice.Token);using var b=Client(bob.Token);
    var source=new LibraryStore(Path.Combine(root,"source"));
    var png=Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jRZkAAAAASUVORK5CYII=");
    var webp=new byte[38];"RIFF"u8.CopyTo(webp);BinaryPrimitives.WriteUInt32LittleEndian(webp.AsSpan(4),30);"WEBPVP8X"u8.CopyTo(webp.AsSpan(8));BinaryPrimitives.WriteUInt32LittleEndian(webp.AsSpan(16),10);webp[24]=webp[27]=255;webp[25]=webp[28]=1;"VP8L"u8.CopyTo(webp.AsSpan(30));
    var pack=await source.CreatePackAsync("Friends layered pack","Publisher fixture");
    var composition=new LayerComposition{Layers=[new(){Id="caption",Name="Text",Source="data:image/png;base64,"+Convert.ToBase64String(png),Text=new(){Content="TOP TEXT 😎",Bold=true},Rotation=15}]};
    await source.SaveCompositionAsync(null,"Layered fixture",webp,composition,pack);
    var privateBytes=await Export();
    Require((await Put(a,pack,privateBytes)).StatusCode==HttpStatusCode.BadRequest,"Server accepted a private pack");
    Require((await Feed(b)).Items.Count==0,"Private upload appeared in the feed");
    await source.SetPackPublicAsync(pack,true);var bytes=await Export();
    Require((await Put(a,pack,bytes)).IsSuccessStatusCode,"Public layered upload failed");
    var feed=await Feed(b);Require(feed.Items.Count==1 && feed.Items[0].OwnerId==alice.Id && feed.Items[0].Username=="Alice fixture","Invited friend cannot browse correct author/pack metadata");
    var download=$"/api/friends/packs/{alice.Id}/{pack}/download";var preview=$"/api/friends/packs/{alice.Id}/{pack}/preview";
    Require((await anonymous.GetAsync(download)).StatusCode==HttpStatusCode.Unauthorized && (await anonymous.GetAsync(preview)).StatusCode==HttpStatusCode.Unauthorized,"A direct pack/preview URL bypasses membership");
    var response=await b.GetAsync(download);Require(response.IsSuccessStatusCode && response.Headers.CacheControl?.NoStore==true,"Authenticated download/cache controls failed");
    var downloaded=await response.Content.ReadAsByteArrayAsync();Require(downloaded.SequenceEqual(bytes),"Download changed ZIP bytes");
    var target=new LibraryStore(Path.Combine(root,"target"));using(var stream=new MemoryStream(downloaded))await target.ImportPackAsync(stream, forcePrivate:true);
    Require(!(await target.ReadAsync()).Packs.Single().Public,"Imported friends pack must be a private copy to avoid accidental re-publishing");
    var restored=(await target.ReadAsync()).Stickers.Single();Require((await target.ReadCompositionAsync(restored.Id))!.Layers[0].Text!.Content=="TOP TEXT 😎","Friends import lost editable text/layers");
    Require((await b.GetAsync(preview)).IsSuccessStatusCode,"Authenticated preview failed");
    var hash=Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();Require((await anonymous.GetAsync($"/p/{hash}/download")).StatusCode==HttpStatusCode.NotFound,"Friends ZIP leaked through anonymous legacy sharing");
    await b.DeleteAsync($"/api/friends/packs/{pack}");Require((await Feed(a)).Items.Count==1,"Another member can delete someone else's pack");
    Require((await Put(b,pack,bytes)).IsSuccessStatusCode && (await Feed(a)).Items.Count==2,"Owner scoping failed for matching local pack IDs");
    await source.RenamePackAsync(pack,"Updated friends pack");var updated=await Export();await Put(a,pack,updated);
    var updatedFeed=await Feed(b);Require(updatedFeed.Items.Count==2 && updatedFeed.Items.Single(p=>p.OwnerId==alice.Id).Name=="Updated friends pack","Sync did not replace the owner’s previous revision");
    await a.DeleteAsync($"/api/friends/packs/{pack}");
    Require((await Feed(b)).Items.All(p=>p.OwnerId!=alice.Id),"Private/removed pack remains in overview");
    Require((await b.GetAsync(download)).StatusCode==HttpStatusCode.NotFound && (await b.GetAsync(preview)).StatusCode==HttpStatusCode.NotFound,"Hidden pack remains downloadable by its old URL");
    Require((await b.GetAsync($"/api/friends/packs/{bob.Id}/{pack}/download")).IsSuccessStatusCode,"Removing one owner’s pack removed another owner’s pack");
    await CollectionChecks.Run(anonymous,a,b,source,pack,composition,webp);
    await RepositoryChecks.Run(root);
    await b.DeleteAsync("/api/friends/membership");
    Require((await b.GetAsync("/api/friends/packs")).StatusCode==HttpStatusCode.Unauthorized,"Leaving the group did not revoke its token");
    Require((await Feed(a)).Items.Count==0,"Leaving the group did not remove owned packs");
    await a.DeleteAsync("/api/friends/membership");
    Console.WriteLine("PASS invitations, anonymous denial, private upload rejection, author ownership, revision replacement and visibility enforcement on feed/preview/download");
    Console.WriteLine("PASS friends-only archives cannot use legacy public links; authenticated editable-layer download/import preserves ZIP bytes and text");
    async Task<byte[]> Export(){using var stream=new MemoryStream();await source.ExportPackAsync(pack,stream);return stream.ToArray();}
}
finally {if(Directory.Exists(root))Directory.Delete(root,true);}
async Task<Member> Join(string username){var response=await anonymous.PostAsJsonAsync("/api/friends/join",new{inviteCode=invitation,username});response.EnsureSuccessStatusCode();return (await response.Content.ReadFromJsonAsync<Member>())!;}
HttpClient Client(string token){var client=new HttpClient{BaseAddress=serverUri};client.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",token);return client;}
static Task<HttpResponseMessage> Put(HttpClient client,Guid id,byte[] bytes)=>client.PutAsync($"/api/friends/packs/{id}",new ByteArrayContent(bytes));
static async Task<Overview> Feed(HttpClient client)=>(await client.GetFromJsonAsync<Overview>("/api/friends/packs"))!;
static void Require(bool value,string message){if(!value)throw new Exception(message);}
record Member(Guid Id,string Username,string Token);
record Card(Guid OwnerId,Guid PackId,string Username,string Name);
record Overview(List<Card> Items,int Total);
