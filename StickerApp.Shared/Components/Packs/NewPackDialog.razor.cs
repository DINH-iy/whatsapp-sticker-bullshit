using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using StickerApp.Core;

namespace StickerApp.Shared.Components.Packs;

public partial class NewPackDialog
{
    [Parameter] public EventCallback<Guid> OnCreated { get; set; }
    [Parameter] public StickerPack? Pack { get; set; }
    [Parameter] public EventCallback<Guid> OnSaved { get; set; }
    [Parameter] public EventCallback OnCancel { get; set; }
    private ElementReference dialog;
    private IJSObjectReference? dialogHelper;
    private string name = "";
    private string publisher = "";
    private string? error;
    private bool saving;

    protected override void OnInitialized()
    {
        name = Pack?.Name ?? "";
        publisher = Pack?.Publisher ?? "";
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender) return;
        dialogHelper = await JS.InvokeAsync<IJSObjectReference>("import", "./_content/StickerApp.Shared/js/ui/dialog.js");
        await dialogHelper.InvokeVoidAsync("open", dialog);
    }

    private async Task CreatePack()
    {
        if (saving || string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(publisher)) return;
        saving = true;
        error = null;
        try
        {
            if (Pack is { } pack)
            {
                await Store.UpdatePackDetailsAsync(pack.Id, name, publisher);
                await OnSaved.InvokeAsync(pack.Id);
            }
            else
            {
                var id = await Store.CreatePackAsync(name, publisher);
                await OnCreated.InvokeAsync(id);
            }
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
