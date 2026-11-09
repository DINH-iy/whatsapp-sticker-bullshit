using System.Reflection;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using StickerApp.Core;
using StickerApp.Shared.Components.Settings;
using StickerApp.Shared.Services;

internal static class ResetRestartChecks
{
    public static async Task<int> RunAsync()
    {
        foreach (var mode in new[] { "native", "web", "native-failure" })
        {
            var root=Path.Combine(Path.GetTempPath(),"stik-reset-restart-"+Guid.NewGuid().ToString("N"));
            try
            {
                var store=new LibraryStore(root);
                await store.ImportAsync("Fixture.png",Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jRZkAAAAASUVORK5CYII="));
                await store.SaveProfileAsync(new("Fixture","Reset",null,true));
                var js=new ResetJs();var native=new ResetNative(js.Module,mode=="native-failure");
                var builder=new ServiceCollection().AddLogging().AddSingleton(store).AddSingleton(new AppLog(Path.Combine(root,"logs")))
                    .AddSingleton<AppLanguage>().AddSingleton<IJSRuntime>(js).AddSingleton<IFormFactor>(new ResetFormFactor(mode));
                if(mode!="web")builder.AddSingleton<IAppRestart>(native);
                await using var services=builder.BuildServiceProvider();
                await using var renderer=new HtmlRenderer(services,services.GetRequiredService<ILoggerFactory>());
                FactoryResetSettings component=null!;
                await renderer.Dispatcher.InvokeAsync(async()=>await renderer.RenderComponentAsync<ResetHarness>(ParameterView.FromDictionary(new Dictionary<string,object?> {
                    ["Capture"]=(Action<FactoryResetSettings>)(value=>component=value)
                })));
                var flags=BindingFlags.NonPublic|BindingFlags.Instance;
                var reset=typeof(FactoryResetSettings).GetMethod("Reset",flags)!;
                await renderer.Dispatcher.InvokeAsync(()=>(Task)reset.Invoke(component,null)!);
                Require(js.Imports==0&&(await store.ReadAsync()).Stickers.Count==1,"Reset bypassed the confirmations");
                typeof(FactoryResetSettings).GetField("stage",flags)!.SetValue(component,2);
                typeof(FactoryResetSettings).GetField("confirmation",flags)!.SetValue(component,"RESET");
                await renderer.Dispatcher.InvokeAsync(()=>(Task)reset.Invoke(component,null)!);
                Require((await store.ReadAsync()).Stickers.Count==0&&!(await store.ReadProfileAsync()).SetupComplete,"Reset did not prepare first-launch state");
                Require(js.Module.Cleared&&js.Module.Disposed,"Reset did not clear preferences and release JS references");
                if(mode=="web")Require(js.Module.BrowserRestart&&!native.Called,"Web reset did not keep browser navigation");
                else Require(native.Called&&!js.Module.BrowserRestart,"Native reset reloaded HTML instead of restarting its host");
                var error=typeof(FactoryResetSettings).GetField("error",flags)!.GetValue(component) as string;
                Require(mode=="native-failure" ? error?.StartsWith("Library data was erased")==true : error is null,"Restart failure was hidden or successful restart reported an error");
                Console.WriteLine("PASS factory reset restart: "+mode);
            }
            finally {if(Directory.Exists(root))Directory.Delete(root,true);}
        }
        return 3;
    }
    private static void Require(bool condition,string message){if(!condition)throw new Exception(message);}
    private sealed class ResetFormFactor(string mode):IFormFactor
    {
        public string GetFormFactor()=>mode=="web"?"Web":"Phone";
        public string GetPlatform()=>"Fixture";
    }
    private sealed class ResetNative(ResetModule module,bool fail):IAppRestart
    {
        public bool Called;
        public Task RestartAsync(){Require(module.Cleared&&module.Disposed,"Native host replaced before releasing the old JS module");Called=true;if(fail)throw new InvalidOperationException("Native restart fixture failed.");return Task.CompletedTask;}
    }
    private sealed class ResetJs:IJSRuntime
    {
        public readonly ResetModule Module=new();public int Imports;
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier,object?[]? args)=>InvokeAsync<TValue>(identifier,default,args);
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier,CancellationToken token,object?[]? args)
        {Require(identifier=="import","Unexpected reset JS call");Imports++;return ValueTask.FromResult((TValue)(object)Module);}
    }
    private sealed class ResetModule:IJSObjectReference
    {
        public bool Cleared,Disposed,BrowserRestart;
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier,object?[]? args)=>InvokeAsync<TValue>(identifier,default,args);
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier,CancellationToken token,object?[]? args)
        {if(identifier=="clearPreferences")Cleared=true;else if(identifier=="restart"){Require(Cleared,"Browser restarted before preferences were cleared");BrowserRestart=true;}else throw new Exception("Unexpected reset module call");return ValueTask.FromResult(default(TValue)!);}
        public ValueTask DisposeAsync(){Disposed=true;return ValueTask.CompletedTask;}
    }
}

internal sealed class ResetHarness:ComponentBase
{
    [Parameter] public Action<FactoryResetSettings> Capture {get;set;}=null!;
    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {builder.OpenComponent<FactoryResetSettings>(0);builder.AddComponentReferenceCapture(1,value=>Capture((FactoryResetSettings)value));builder.CloseComponent();}
}
