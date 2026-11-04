using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace StickerApp.Shared.Pages;

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

    protected override void OnInitialized() => folder = Store.Root;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender)
            return;
        try
        {
            theme = await JS.InvokeAsync<string>("stickerTheme.get");
            color = await JS.InvokeAsync<string>("stickerTheme.getColor");
            ready = true;
        }
        catch (JSException)
        {
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
        catch (JSException)
        {
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
        catch (JSException)
        {
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
        catch (UnauthorizedAccessException)
        {
            storageError = "The app does not have permission to write to this folder. Choose another folder.";
        }
        catch (Exception e) when (e is IOException or InvalidOperationException or ArgumentException or NotSupportedException or System.Text.Json.JsonException)
        {
            storageError = e.Message;
        }
        finally
        {
            saving = false;
        }
    }
}
