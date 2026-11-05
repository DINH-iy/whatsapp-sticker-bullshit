using Microsoft.AspNetCore.Components.Routing;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace StickerApp.Shared.Layout;

public partial class MainLayout
{
    private bool menuOpen;
    private ElementReference menuToggle;

    protected override void OnInitialized() => Navigation.LocationChanged += OnLocationChanged;

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

    public void Dispose() => Navigation.LocationChanged -= OnLocationChanged;
}
