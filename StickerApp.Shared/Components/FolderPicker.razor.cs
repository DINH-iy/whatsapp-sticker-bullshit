using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;

namespace StickerApp.Shared.Components;

public partial class FolderPicker
{
    [Parameter, EditorRequired] public string InitialFolder { get; set; } = "";
    [Parameter]
    public EventCallback<string> OnSelected { get; set; }
    [Parameter]
    public EventCallback OnCancel { get; set; }
    private string current = "", newName = "";
    private string? error;
    private List<string> directories = [];
    private ElementReference dialog;
    private IJSObjectReference? module;
    private string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) is { Length: > 0 } home ? home : InitialFolder;
    private string? Parent => string.IsNullOrEmpty(current) ? null : Directory.GetParent(current)?.FullName;

    protected override void OnInitialized() => Navigate(InitialFolder);

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender)
            return;
        module = await JS.InvokeAsync<IJSObjectReference>("import", "./_content/StickerApp.Shared/folder-picker.js");
        await module.InvokeVoidAsync("open", dialog);
    }

    private void Navigate(string path)
    {
        error = null;
        try
        {
            var fullPath = Path.GetFullPath(path);
            var entries = Directory.EnumerateDirectories(fullPath)
                .OrderBy(p => Path.GetFileName(p), StringComparer.OrdinalIgnoreCase).ToList();
            current = fullPath;
            directories = entries;
            newName = "";
        }
        catch (UnauthorizedAccessException)
        {
            error = "The app cannot access this folder. Choose another folder.";
        }
        catch (Exception e) when (e is IOException or ArgumentException or NotSupportedException)
        {
            error = e.Message;
        }
    }

    private void GoUp()
    {
        if (Parent is { } parent)
            Navigate(parent);
    }
    private void CreateFolder()
    {
        error = null;
        var name = newName.Trim();
        if (string.IsNullOrEmpty(current) || name is "" or "." or ".." || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.Contains('/') || name.Contains('\\'))
        {
            error = "Enter a folder name without slashes.";
            return;
        }
        try
        {
            var path = Path.Combine(current, name);
            if (Directory.Exists(path) || File.Exists(path))
            {
                error = "A folder or file with that name already exists.";
                return;
            }
            Directory.CreateDirectory(path);
            Navigate(path);
        }
        catch (UnauthorizedAccessException)
        {
            error = "The app cannot create a folder here. Choose another location.";
        }
        catch (Exception e) when (e is IOException or ArgumentException or NotSupportedException)
        {
            error = e.Message;
        }
    }

    private Task Choose() => OnSelected.InvokeAsync(current);
    private Task Cancel() => OnCancel.InvokeAsync();
    private Task HandleKey(KeyboardEventArgs e) => e.Key == "Escape" ? Cancel() : Task.CompletedTask;
    public async ValueTask DisposeAsync()
    {
        if (module is null)
            return;
        try
        {
            await module.DisposeAsync();
        }
        catch (JSDisconnectedException) { }
    }
}
