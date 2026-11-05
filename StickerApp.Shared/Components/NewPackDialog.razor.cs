using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;

namespace StickerApp.Shared.Components;

public partial class NewPackDialog
{
    [Parameter] public EventCallback<Guid> OnCreated { get; set; }
    [Parameter] public EventCallback OnCancel { get; set; }
    private ElementReference dialog;
    private IJSObjectReference? dialogHelper;
    private string name = "";
    private string? error;
    private bool saving;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender) return;
        dialogHelper = await JS.InvokeAsync<IJSObjectReference>("import", "./_content/StickerApp.Shared/dialog.js");
        await dialogHelper.InvokeVoidAsync("open", dialog);
    }

    private async Task CreatePack()
    {
        if (saving || string.IsNullOrWhiteSpace(name)) return;
        saving = true;
        error = null;
        try
        {
            var id = await Store.CreatePackAsync(name);
            await OnCreated.InvokeAsync(id);
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            error = exception.Message;
        }
        finally { saving = false; }
    }

    private Task Cancel() => saving ? Task.CompletedTask : OnCancel.InvokeAsync();
    private Task HandleKey(KeyboardEventArgs args) => args.Key == "Escape" ? Cancel() : Task.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        if (dialogHelper is null) return;
        try { await dialogHelper.DisposeAsync(); }
        catch (JSDisconnectedException) { }
    }
}
