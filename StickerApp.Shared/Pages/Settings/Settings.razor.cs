using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;

namespace StickerApp.Shared.Pages.Settings;

public partial class Settings
{
    private string theme = "system";
    private string color = "green";
    private string folder = "";
    private static readonly (string Id, string Label, string Swatch)[] ColorOptions =
    [
        ("green", "Green", "#32875f"),
        ("blue", "Blue", "#447de0"),
        ("purple", "Purple", "#9564cd"),
        ("rose", "Rose", "#d05c89"),
        ("amber", "Amber", "#d89b2b")
    ];
    private bool ready;
    private bool saving;
    private bool showFolderPicker;
    private string? themeError;
    private string? storageError;
    private string? notice;
    private string selectedTab = "general";
    private ElementReference generalTab, developerTab;

    protected override void OnInitialized()
    {
        folder = Store.Root;
        Navigation.LocationChanged += SettingsLocationChanged;
    }

    protected override void OnParametersSet() => ReadTab();

    private void ReadTab()
    {
        var uri = new Uri(Navigation.Uri);
        var fragment = uri.Fragment;
        var developer = uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries).Contains("tab=developer", StringComparer.Ordinal);
        selectedTab = fragment is "#faq" or "#touch-feedback" ? "general" : developer || fragment is "#diagnostics" or "#gif-search" or "#pack-sharing" or "#storage" or "#cloud"
            ? "developer" : "general";
    }

    private void SettingsLocationChanged(object? sender, Microsoft.AspNetCore.Components.Routing.LocationChangedEventArgs args)
    {
        // Fragment-only navigation also needs to reveal the section's tab.
        _ = InvokeAsync(() => { ReadTab(); StateHasChanged(); });
    }

    private void SelectTab(string value)
    {
        selectedTab = value;
        Navigation.NavigateTo(value == "developer" ? "settings?tab=developer" : "settings", replace: true);
    }

    private async Task HandleTabKey(KeyboardEventArgs args)
    {
        if (args.Key is not ("ArrowLeft" or "ArrowRight")) return;
        SelectTab(selectedTab == "general" ? "developer" : "general");
        await (selectedTab == "general" ? generalTab : developerTab).FocusAsync();
    }

    public void Dispose() => Navigation.LocationChanged -= SettingsLocationChanged;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender)
            return;
        try
        {
            theme = await JS.InvokeAsync<string>("stickerTheme.get");
            color = await JS.InvokeAsync<string>("stickerTheme.getColor");
            ready = true;
            ReadTab();
        }
        catch (JSException exception)
        {
            Log.Error("Appearance settings", exception);
            themeError = "Theme settings could not be loaded. Reload the app to try again.";
        }
        StateHasChanged();
    }

    private async Task ChangeTheme(ChangeEventArgs e)
    {
        themeError = null;
        try
        {
            var value = e.Value?.ToString() ?? "system";
            if (await JS.InvokeAsync<bool>("stickerTheme.set", value))
                theme = value;
            else
                themeError = "Your device could not save this theme preference. Check that local storage is enabled.";
        }
        catch (JSException exception)
        {
            Log.Error("Appearance settings", exception);
            themeError = "Could not save the theme. Try reloading the app.";
        }
    }

    private async Task ChangeColor(string selectedColor)
    {
        themeError = null;
        try
        {
            if (await JS.InvokeAsync<bool>("stickerTheme.setColor", selectedColor))
            {
                color = selectedColor;
            }
            else
            {
                themeError = "Your device could not save the color theme. Check that local storage is enabled.";
            }
        }
        catch (JSException exception)
        {
            Log.Error("Appearance settings", exception);
            themeError = "Could not save the color theme. Try reloading the app.";
        }
    }

    private void FolderSelected(string selected)
    {
        folder = selected;
        showFolderPicker = false;
        storageError = notice = null;
    }

    private async Task SaveFolder()
    {
        if (saving)
            return;
        saving = true;
        storageError = notice = null;
        try
        {
            var previous = Store.Root;
            await Store.ChangeFolderAsync(folder.Trim());
            folder = Store.Root;
            notice = previous == Store.Root ? "This folder is already in use." : "Save folder updated. Your library was copied successfully; the original is still available as a backup.";
        }
        catch (UnauthorizedAccessException exception)
        {
            Log.Error("Storage settings", exception);
            storageError = "The app does not have permission to write to this folder. Choose another folder.";
        }
        catch (Exception e) when (e is IOException or InvalidOperationException or ArgumentException or NotSupportedException or System.Text.Json.JsonException)
        {
            Log.Error("Storage settings", e);
            storageError = e.Message;
        }
        finally
        {
            saving = false;
        }
    }
}
