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
var services=new ServiceCollection().AddLogging().AddSingleton(connection).AddSingleton<IJSRuntime,NoJs>().BuildServiceProvider();
await using var renderer=new HtmlRenderer(services,services.GetRequiredService<ILoggerFactory>());
var offline=await renderer.Dispatcher.InvokeAsync(async()=>{
    var view=await renderer.RenderComponentAsync<OnlineOnly>(ParameterView.FromDictionary(new Dictionary<string,object?>{{"ChildContent",(RenderFragment)(b=>b.AddMarkupContent(0,"<button>Online import</button>"))}}));
    return view.ToHtmlString();});
Check(offline.Contains("disabled")&&offline.Contains("No internet connection")&&offline.Contains("Online import"),"offline controls remain visible, disabled and explained");
var banner=await renderer.Dispatcher.InvokeAsync(async()=> (await renderer.RenderComponentAsync<ConnectionStatus>()).ToHtmlString());
Check(banner.Contains("No internet connection")&&banner.Contains("Saved stickers"),"offline startup warns and explains local functionality");
connection.Update(true);
var online=await renderer.Dispatcher.InvokeAsync(async()=> (await renderer.RenderComponentAsync<OnlineOnly>()).ToHtmlString());
Check(!online.Contains("disabled")&&!online.Contains("No internet connection"),"connection restoration enables online controls");
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
