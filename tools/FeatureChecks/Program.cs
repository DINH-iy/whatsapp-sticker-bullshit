using System.Net;
using System.Text;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using StickerApp.Core;
using StickerApp.Shared.Services;
using StickerApp.Shared.Components.Common;
using StickerApp.Shared.Components.Infrastructure;

int passed=0;
void Check(bool condition,string message){if(!condition)throw new Exception(message);passed++;Console.WriteLine("PASS "+message);}
async Task Reject(Func<Task> action,string message){try{await action();throw new Exception("accepted invalid input: "+message);}catch(InvalidDataException){Check(true,message);}}
using var client=new ImgurImportClient(new HttpClient(new FakeHandler()));
foreach(var url in new[]{"http://i.imgur.com/a.gif","https://imgur.com.evil.test/a","https://user@imgur.com/a","https://127.0.0.1/a","https://i.imgur.com:444/a.gif"})
    await Reject(()=>client.PreviewAsync(url,"",default),"reject unsafe Imgur link "+url);
var direct=await client.PreviewAsync("https://i.imgur.com/test.gifv","",default);
Check(direct.Single().Video&&direct[0].Url=="https://i.imgur.com/test.mp4","GIFV uses existing video conversion path");
await Reject(()=>client.PreviewAsync("https://imgur.com/a/abcde","",default),"album requires Client ID");
var album=await client.PreviewAsync("https://imgur.com/a/abcde","client123",default);
Check(album.Count==2&&!album[0].Video&&album[1].Video,"album metadata includes images and videos");
Check((await client.DownloadAsync(album[0],default)).Length>0,"bounded media download validates image bytes");
await Reject(()=>client.DownloadAsync(new("bad","https://example.com/private.png",false),default),"media URL cannot fetch an arbitrary host");
await Reject(()=>client.PreviewAsync("https://imgur.com/a/large","client123",default),"oversized album rejected before media downloads");
await Reject(()=>client.DownloadAsync(new("large","https://i.imgur.com/large.png",false),default),"image size checked before reading body");
var connection=new ConnectionState();connection.Update(false);
var services=new ServiceCollection().AddSingleton<AppLanguage>().AddLogging().AddSingleton(connection).AddSingleton<IJSRuntime,NoJs>().BuildServiceProvider();
await using var renderer=new HtmlRenderer(services,services.GetRequiredService<ILoggerFactory>());
var offline=await renderer.Dispatcher.InvokeAsync(async()=>{
    var view=await renderer.RenderComponentAsync<OnlineOnly>(ParameterView.FromDictionary(new Dictionary<string,object?>{{"ChildContent",(RenderFragment)(b=>b.AddMarkupContent(0,"<button>Online import</button>"))}}));
    return view.ToHtmlString();});
Check(offline.Contains("disabled")&&offline.Contains("No internet connection")&&offline.Contains("Online import")&&offline.Contains("is-offline")&&offline.Contains("<svg"),"offline controls remain visible, disabled and explained");
var banner=await renderer.Dispatcher.InvokeAsync(async()=> (await renderer.RenderComponentAsync<ConnectionStatus>()).ToHtmlString());
Check(banner.Contains("No internet connection")&&banner.Contains("Saved stickers"),"offline startup warns and explains local functionality");
var offlineNav=await renderer.Dispatcher.InvokeAsync(async()=> (await renderer.RenderComponentAsync<OnlineNavLink>(ParameterView.FromDictionary(new Dictionary<string,object?>{{"Href","gifs"},{"Label","GIF search"}}))).ToHtmlString());
Check(offlineNav.Contains("aria-disabled=\"true\"")&&offlineNav.Contains("<svg")&&!offlineNav.Contains("href="),"offline navigation shows a disconnected icon and cannot navigate");
connection.Update(true);
var online=await renderer.Dispatcher.InvokeAsync(async()=> (await renderer.RenderComponentAsync<OnlineOnly>()).ToHtmlString());
Check(!online.Contains("disabled")&&!online.Contains("No internet connection"),"connection restoration enables online controls");
services.GetRequiredService<AppLanguage>().Set("en");
var timingBars=await renderer.Dispatcher.InvokeAsync(async()=> (await renderer.RenderComponentAsync<StickerApp.Shared.Components.Settings.PerformanceChart>(ParameterView.FromDictionary(new Dictionary<string,object?>{{"Entries",new AppLog.PerformanceEntry[]{new(DateTimeOffset.UtcNow,"Opening",250,""),new(DateTimeOffset.UtcNow,"Decoding",1500,""),new(DateTimeOffset.UtcNow,"Video: full GIF encoding",17300,"")}}}))).ToHtmlString());
Check(timingBars.Contains("timing-fast")&&timingBars.Contains("timing-medium")&&timingBars.Contains("timing-slow")&&timingBars.Contains(17.3.ToString("0.00"))&&timingBars.Contains("Creating GIF"),"performance chart labels slow operations and distinguishes fast/noticeable/slow without color alone");
using var discord = new DiscordImportClient(new HttpClient(new DiscordHandler()));
foreach (var url in new[] { "https://127.0.0.1/a.gif", "http://media.tenor.com/a.gif", "https://media.tenor.com.evil.test/a.gif", "https://user@media.tenor.com/a.gif" })
    await Reject(() => discord.DownloadAsync(new() { MediaUrl = url }, 100000, default), "Discord rejects unsafe URL " + url);
var favorite = new DiscordFavorite { Id = "a", Name = "Favourite", MediaUrl = "https://media.tenor.com/a.gif" };
Check(!(await discord.DownloadAsync(favorite, 100000, default)).Video, "Discord validates downloaded image bytes");
await Reject(() => discord.DownloadAsync(new() { MediaUrl = "https://media.tenor.com/expired" }, 100000, default), "Discord explains expired links");
await Reject(() => discord.DownloadAsync(new() { MediaUrl = "https://media.tenor.com/html" }, 100000, default), "Discord rejects provider HTML");
await Reject(() => discord.DownloadAsync(new() { MediaUrl = "https://media.tenor.com/redirect" }, 100000, default), "Discord does not accept redirects");
await Reject(() => discord.DownloadAsync(favorite, 1, default), "Discord enforces remaining batch size");
var discordRoot = Path.Combine(Path.GetTempPath(), "stik-discord-check-" + Guid.NewGuid().ToString("N"));
try
{
    var store = new LibraryStore(discordRoot);
    await discord.ImportAsync(store, [favorite], null, (_, _, _) => throw new Exception("Image should not open video editor"), null, default);
    Check((await store.ReadAsync()).Stickers.Count == 1, "Discord commits selected image batch");
    var video = new DiscordFavorite { Id = "b", Name = "Video", MediaUrl = "https://media.tenor.com/video.mp4", Video = true };
    var reviewed = false;
    try
    {
        await discord.ImportAsync(store, [favorite, video], null, (_, _, _) => { reviewed = true; throw new OperationCanceledException(); }, null, default);
        throw new Exception("Cancelled review was accepted");
    }
    catch (OperationCanceledException) { }
    Check(reviewed && (await store.ReadAsync()).Stickers.Count == 1, "Cancelling reused video review leaves entire batch unsaved");
}
finally { if (Directory.Exists(discordRoot)) Directory.Delete(discordRoot, true); }
var soloPack = new StickerPack { Name="A solo", Public=true };
var groupPack = new StickerPack { Name="Z group", Collection=new("https://example.test/",Guid.NewGuid(),1) };
var packSnapshot = new LibraryBrowseSnapshot(new StickerLibrary { Packs=[soloPack,groupPack] },new Dictionary<Guid,ImageStorageSize>(),new Dictionary<Guid,PackStorageStatistics>());
Check(LibraryBrowsing.Packs(packSnapshot,new(Ownership:"group"),false).Single().Id==groupPack.Id,"group filter selects working copies independently of public visibility");
Check(LibraryBrowsing.Packs(packSnapshot,new(Ownership:"solo"),false).Single().Id==soloPack.Id,"solo filter includes public personal packs");
Check(LibraryBrowsing.Packs(packSnapshot,new(Sort:"group-first"),false).First().Id==groupPack.Id,"group-first sorting keeps solo packs and prioritizes group copies");
Check(LibraryBrowsing.Packs(packSnapshot,new(Sort:"solo-first"),false).First().Id==soloPack.Id,"solo-first sorting keeps group copies and prioritizes personal packs");
Check(LibraryBrowsing.Packs(packSnapshot,new(Search:"solo",Ownership:"group"),false).Count==0,"ownership filters combine with search");
var maintenance=await renderer.Dispatcher.InvokeAsync(async()=> (await renderer.RenderComponentAsync<FriendsMaintenance>()).ToHtmlString());
Check(FriendsFeature.UnderMaintenance && maintenance.Contains("Under maintenance") && !maintenance.Contains("Create invite link"),"maintenance screen explains deferred collaboration without join controls");
var languages=new AppLanguage();
foreach(var (code,expected) in new[]{("nl","Instellingen"),("de","Einstellungen"),("ro","Setări"),("nb","Innstillinger")}){languages.Set(code);Check(languages["Settings"]==expected,"JSON language catalog loads "+code);}
Check(languages["Untranslated example"]=="Untranslated example","missing translations fall back without losing text");
languages.Set("unsupported");Check(languages.Code=="en" && languages["Settings"]=="Settings","unsupported language safely falls back to English");
// UI translation is a presentation boundary; source diagnostics are never localized.
var resources=typeof(AppLanguage).Assembly;
var catalogs=AppLanguage.Supported.ToDictionary(item=>item.Code,item=>{
    using var stream=resources.GetManifestResourceStream($"StickerApp.Shared.Localization.{item.Code}.json")!;
    return System.Text.Json.JsonSerializer.Deserialize<Dictionary<string,string>>(stream)!;
});
foreach(var code in new[]{"nl","de","ro","nb"})
{
    languages.Set(code);
    Check(catalogs[code].Keys.ToHashSet().SetEquals(catalogs["en"].Keys),"catalog key parity "+code);
    Check(catalogs[code].All(entry=>!string.IsNullOrWhiteSpace(entry.Value)),"no empty translations "+code);
    Check(catalogs[code].All(entry=>System.Text.RegularExpressions.Regex.Matches(entry.Value,@"\{[^{}]+\}").Select(match=>match.Value).Order().SequenceEqual(System.Text.RegularExpressions.Regex.Matches(catalogs["en"][entry.Key],@"\{[^{}]+\}").Select(match=>match.Value).Order())),"translation placeholders retained "+code);
    Check(languages["The canvas is always 512 × 512 units. Small images keep their size; checkerboard areas are transparent."]!=catalogs["en"]["The canvas is always 512 × 512 units. Small images keep their size; checkerboard areas are transparent."],"editor help translated "+code);
    var raw="This pack has room for 7 more stickers. Select fewer images or choose My Library.";
    Check(languages.Error(raw)!=raw&&languages.Error(raw).Contains("7"),"dynamic capacity warning translated "+code);
    var source="Photo with Settings in its name: convert to WebP (WhatsApp's sticker format); compress from 233 KB to at most 100 KB. Automatic preparation is pending; open sticker details for its status.";
    var message=languages.Message(source);
    Check(message.StartsWith("Photo with Settings in its name:")&&!message.Contains("Automatic preparation")&&!message.Contains("convert to WebP")&&message.Contains("233")&&message.Contains("100"),"nested pack warning preserves artwork name and numbers "+code);
    var error=languages.Error("Unknown vendor exception. Reference: abc123def456.");
    Check(error.Contains("abc123def456")&&!error.Contains("Unknown vendor exception"),"unknown technical error gets localized explanation and preserved reference "+code);
    var known=languages.Error("This image no longer exists.");
    Check(languages.Error(known)==known,"already translated errors are not translated again "+code);
    Check(languages.Format("By {0}","Settings") .EndsWith("Settings"),"formatted user text remains untouched "+code);
}
var logRoot=Path.Combine(Path.GetTempPath(),"stik-language-log-"+Guid.NewGuid().ToString("N"));
try
{
    var log=new AppLog(logRoot);
    foreach(var code in new[]{"en","nl","de","ro","nb"})
    {
        languages.Set(code);
        var original=new InvalidDataException("This image no longer exists.");
        var reference=log.Error("Open image editor",original);
        _=languages.Error(original.Message+" Reference: "+reference+".");
        var report=log.Report();
        Check(report.Contains("Open image editor")&&report.Contains(original.Message)&&report.Contains(reference)&&!report.Contains("Deze afbeelding bestaat niet meer."),"diagnostic logs and reports remain English in "+code);
    }
}
finally { if(Directory.Exists(logRoot))Directory.Delete(logRoot,true); }
var deleteRoot=Path.Combine(Path.GetTempPath(),"stik-delete-pack-check-"+Guid.NewGuid().ToString("N"));
try
{
    var store=new LibraryStore(deleteRoot);
    var packId=await store.CreatePackAsync("Delete only this pack");
    var imageId=await store.ImportAsync("Keep this sticker",Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVQIHWP4z8DwHwAFgAI/ScLttAAAAABJRU5ErkJggg=="),packId);
    var before=await store.ReadAsync();var file=before.Stickers.Single().FileName;
    await store.DeletePackAsync(packId);
    var after=await store.ReadAsync();
    Check(after.Packs.Count==0&&after.Stickers.Single().Id==imageId&&after.Stickers.Single().FileName==file,"deleting a pack keeps its stickers in My Library");
}
finally {if(Directory.Exists(deleteRoot))Directory.Delete(deleteRoot,true);}
var resetPreview=await renderer.Dispatcher.InvokeAsync(async()=>
    (await renderer.RenderComponentAsync<StickerApp.Shared.Components.Settings.FactoryResetProgressView>(ParameterView.FromDictionary(new Dictionary<string,object?> {
        ["Progress"]=new FactoryResetProgress("Deleting app files…",37,100),["Step"]=1
    }))).ToHtmlString());
Check(resetPreview.Contains("value=\"37\"")&&resetPreview.Contains("max=\"100\"")&&resetPreview.Contains("37 of 100 files deleted"),"reset progress renders real deletion counts and a determinate progress bar");
Check(resetPreview.Contains("data-reset-stalled")&&resetPreview.Contains("data-reset-elapsed")&&resetPreview.Contains("aria-busy=\"true\""),"reset progress exposes independent elapsed/stall status and accessible busy state");
var preferencesPreview=await renderer.Dispatcher.InvokeAsync(async()=>
    (await renderer.RenderComponentAsync<StickerApp.Shared.Components.Settings.FactoryResetProgressView>(ParameterView.FromDictionary(new Dictionary<string,object?> {
        ["Progress"]=new FactoryResetProgress("Clearing saved preferences…"),["Step"]=2
    }))).ToHtmlString());
Check(preferencesPreview.Contains("Step 2 of 3")&&!preferencesPreview.Contains("<progress"),"preference cleanup has its own named step without a fake file percentage");
if(Environment.GetEnvironmentVariable("STIK_RESET_PREVIEW_PATH") is {} previewPath)
{
    var labels=System.Text.Json.JsonSerializer.Serialize(new {elapsed="Elapsed: {0} s",stalled="No progress update for {0} seconds. This may be taking longer than usual. If it stays stuck, reopen the app. Some data may already have been erased."});
    await File.WriteAllTextAsync(previewPath,"<!doctype html><meta charset=utf-8><meta name=viewport content='width=device-width,initial-scale=1'><title>Reset progress preview</title><link rel=stylesheet href='/_content/StickerApp.Shared/css/app.css'><style>body{min-height:100vh;display:grid;place-items:center;padding:20px;box-sizing:border-box}.factory-reset-dialog{position:relative;margin:0;box-sizing:border-box}.qa-note{margin:0 0 24px;font-size:13px;color:var(--muted)}</style><dialog open class='folder-dialog factory-reset-dialog'><p class=qa-note>Preview only · no data is deleted</p><h2>Resetting the app…</h2>"+resetPreview+"</dialog><script type=module>import {watchProgress} from '/_content/StickerApp.Shared/js/ui/factory-reset.js';watchProgress(document.querySelector('.factory-reset-progress'),"+labels+");</script>");
}
var encodingPopup=await renderer.Dispatcher.InvokeAsync(async()=>
    (await renderer.RenderComponentAsync<OperationProgressDialog>(ParameterView.FromDictionary(new Dictionary<string,object?> {
        ["Title"]="Applying animation edits…",["CanCancel"]=false
    }))).ToHtmlString());
Check(encodingPopup.Contains("<dialog")&&encodingPopup.Contains("Applying animation edits")&&encodingPopup.Contains("data-operation-elapsed")&&encodingPopup.Contains("disabled"),"encoding popup shows the operation, elapsed timer and safe cancellation state");
var preparationNotice=await renderer.Dispatcher.InvokeAsync(async()=>
    (await renderer.RenderComponentAsync<OperationProgressDialog>(ParameterView.FromDictionary(new Dictionary<string,object?> {
        ["Title"]="Preparing sticker…",["Blocking"]=false,["Detail"]="Example GIF"
    }))).ToHtmlString());
Check(preparationNotice.Contains("<aside")&&!preparationNotice.Contains("<dialog")&&!preparationNotice.Contains("<button")&&preparationNotice.Contains("Example GIF"),"background encoding shows a notice without blocking the current editor");
passed+=await ImportDownloadChecks.RunAsync();
passed+=await GifImportRecoveryChecks.RunAsync();
passed+=await GifSizeChecks.RunAsync();
passed+=await ResetRestartChecks.RunAsync();
passed+=await PreparationLifecycleChecks.RunAsync();
passed+=await DiagnosticsPerformanceChecks.RunAsync();
passed+=await DirectVideoImportChecks.RunAsync();
Console.WriteLine($"{passed} feature checks passed.");
sealed class NoJs:IJSRuntime{
    public ValueTask<T> InvokeAsync<T>(string identifier,object?[]? args)=>throw new NotSupportedException();
    public ValueTask<T> InvokeAsync<T>(string identifier,CancellationToken token,object?[]? args)=>throw new NotSupportedException();
}
sealed class FakeHandler:HttpMessageHandler{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token){
        var path=request.RequestUri!.AbsolutePath;
        if(request.RequestUri.Host=="api.imgur.com"){
            if(!request.Headers.TryGetValues("Authorization",out var values)||values.Single()!="Client-ID client123")throw new Exception("missing client id");
            var item="{\"link\":\"https://i.imgur.com/test.png\",\"type\":\"image/png\",\"title\":\"Photo\"}";
            var json=path.EndsWith("large")?"{\"data\":{\"images\":["+string.Join(',',Enumerable.Repeat(item,101))+"]}}":"{\"data\":{\"images\":["+item+",{\"link\":\"https://i.imgur.com/clip.mp4\",\"type\":\"video/mp4\",\"title\":null}]}}";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(json,Encoding.UTF8,"application/json")});
        }
        var bytes=Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVQIHWP4z8DwHwAFgAI/ScLttAAAAABJRU5ErkJggg==");
        var content=new ByteArrayContent(bytes);if(path.Contains("large"))content.Headers.ContentLength=11*1024*1024;
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=content});
    }
}

sealed class DiscordHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        var path = request.RequestUri!.AbsolutePath;
        var status = path == "/expired" ? HttpStatusCode.Forbidden : path == "/redirect" ? HttpStatusCode.Redirect : HttpStatusCode.OK;
        var bytes = path == "/video.mp4" ? new byte[] { 0, 0, 0, 12, 102, 116, 121, 112, 0, 0, 0, 0 } : Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVQIHWP4z8DwHwAFgAI/ScLttAAAAABJRU5ErkJggg==");
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new(path == "/html" ? "text/html" : path == "/video.mp4" ? "video/mp4" : "image/png");
        return Task.FromResult(new HttpResponseMessage(status) { Content = content });
    }
}
