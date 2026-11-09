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
    private bool saving, nsfw, preparingCover, coverChanged;
    private ElementReference coverInput;
    private byte[]? coverBytes;
    private string? coverPreview;

    protected override async Task OnInitializedAsync()
    {
        name = Pack?.Name ?? "";
        coverPreview = Pack?.CoverImage;
        nsfw = Pack?.Nsfw ?? false;
        publisher = Pack?.Publisher ?? (await Store.ReadProfileAsync()).Username;
    }

    protected override async Task OnAfterRenderInteropAsync(bool firstRender)
    {
        if (!firstRender) return;
        dialogHelper = await JS.InvokeAsync<IJSObjectReference>("import", "./_content/StickerApp.Shared/js/ui/dialog.js");
        if (IsDisposed) return;
        await dialogHelper.InvokeVoidAsync("open", dialog);
        if (IsDisposed) return;
    }

    private async Task CreatePack()
    {
        if (saving || preparingCover || string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(publisher)) return;
        saving = true;
        error = null;
        try
        {
            if (Pack is { } pack)
            {
                await Store.UpdatePackDetailsAsync(pack.Id, name, publisher, nsfw, coverChanged, coverBytes);
                await OnSaved.InvokeAsync(pack.Id);
            }
            else
            {
                var id = await Store.CreatePackAsync(name, publisher, nsfw);
                await OnCreated.InvokeAsync(id);
            }
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            error = exception.Message;
        }
        finally { saving = false; }
    }

    private async Task ChooseCover()
    {
        if (saving || preparingCover) return;
        preparingCover = true;
        error = null;
        try
        {
            await using var module = await JS.InvokeAsync<IJSObjectReference>("import", "./_content/StickerApp.Shared/js/packs/pack-cover.js");
            var base64 = await module.InvokeAsync<string?>("prepare", coverInput);
            if (base64 is not null)
            {
                var bytes = Convert.FromBase64String(base64);
                coverPreview = PackCover.FromBytes(bytes);
                coverBytes = bytes;
                coverChanged = true;
            }
        }
        catch (Exception exception) when (exception is JSException or IOException or FormatException)
        {
            error = "Could not read that cover. Choose a JPEG, PNG, or WebP image under 10 MB.";
        }
        finally { preparingCover = false; }
    }

    private void RemoveCover()
    {
        coverBytes = null;
        coverPreview = null;
        coverChanged = true;
    }

    private Task Cancel() => saving || preparingCover ? Task.CompletedTask : OnCancel.InvokeAsync();
    private Task HandleKey(KeyboardEventArgs args) => args.Key == "Escape" ? Cancel() : Task.CompletedTask;

    protected override async ValueTask DisposeInteropAsync()
    {
        if (dialogHelper is null) return;
        try { await dialogHelper.DisposeAsync(); }
        catch (JSDisconnectedException) { }
    }
}
