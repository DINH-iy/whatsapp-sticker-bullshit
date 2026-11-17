using Microsoft.JSInterop;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace StickerApp.Shared.Layout;

public partial class MainLayout
{
    private bool menuOpen,languageReady,showGuide;
    private ElementReference menuToggle;

    protected override void OnInitialized(){Navigation.LocationChanged += OnLocationChanged;L.Changed+=LanguageChanged;}
    private void LanguageChanged()=>_ = InvokeAsync(StateHasChanged);
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if(!firstRender)return;
        try{await using var language=await JS.InvokeAsync<Microsoft.JSInterop.IJSObjectReference>("import","./_content/StickerApp.Shared/js/ui/language.js");L.Set(await language.InvokeAsync<string>("read"));}
        catch(Microsoft.JSInterop.JSException){}
        finally{languageReady=true;StateHasChanged();}
    }

    private string ImportHref(string page)
    {
        var path = Navigation.ToBaseRelativePath(Navigation.Uri).Split('?', '#')[0].TrimEnd('/');
        Guid? packId = path.StartsWith("packs/", StringComparison.OrdinalIgnoreCase) && Guid.TryParse(path[6..], out var id) ? id : null;
        if (packId is null && path is "gifs" or "pinterest" or "imgur" or "discord")
        {
            var query = System.Web.HttpUtility.ParseQueryString(new Uri(Navigation.Uri).Query);
            if (Guid.TryParse(query["pack"], out var queryId)) packId = queryId;
        }
        return packId is { } destination ? $"{page}?pack={destination}" : page;
    }

    private void ToggleMenu() => menuOpen = !menuOpen;

    private void CloseMenu() => menuOpen = false;

    private async Task HandleMenuKey(KeyboardEventArgs args)
    {
        if (args.Key == "Escape" && menuOpen)
        {
            menuOpen = false;
            await menuToggle.FocusAsync();
        }
    }

    private void OnLocationChanged(object? sender, LocationChangedEventArgs args)
    {
        menuOpen = false;
        _ = InvokeAsync(StateHasChanged);
    }

    public void Dispose(){Navigation.LocationChanged -= OnLocationChanged;L.Changed-=LanguageChanged;}
}
