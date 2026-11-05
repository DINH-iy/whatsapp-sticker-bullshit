using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using StickerApp.Core;
using StickerApp.Shared.Services;

namespace StickerApp.Shared.Components.Packs;

public partial class SharePackDialog
{
    [Parameter, EditorRequired] public StickerPack Pack { get; set; } = default!;
    [Parameter] public EventCallback OnClose { get; set; }
    [Parameter] public bool PreparationPending { get; set; }
    private bool WaitingForPreparation => format == "wastickers" && PreparationPending;

    private ElementReference dialog;
    private IJSObjectReference? dialogHelper;
    private IJSObjectReference? sharingClient;
    private PackSharingService.ShareResult? result;
    private string format = "wastickers";
    private bool isReady;
    private bool isWorking;
    private bool canPublish;
    private string progress = "";
    private string? error;
    private string? copyNotice;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender) return;
        try
        {
            dialogHelper = await JS.InvokeAsync<IJSObjectReference>("import", "./_content/StickerApp.Shared/js/ui/dialog.js");
            await dialogHelper.InvokeVoidAsync("open", dialog);
            sharingClient = await JS.InvokeAsync<IJSObjectReference>("import", "./_content/StickerApp.Shared/js/ui/pack-sharing.js");
            var settings = await sharingClient.InvokeAsync<PackSharingSettings.SharingSettings>("getSettings");
            canPublish = settings.HasToken;
            isReady = true;
        }
        catch (JSException exception)
        {
            Log.Error("Load pack sharing", exception);
            error = "Sharing could not be loaded. Reload the app to try again.";
        }
        StateHasChanged();
    }

    private Task SaveFile() => RunShare(publish: false);
    private Task PublishLink() => RunShare(publish: true);

    private async Task RunShare(bool publish)
    {
        if (isWorking) return;
        isWorking = true;
        result = null;
        copyNotice = error = null;
        progress = publish ? "Preparing and uploading pack…" : "Preparing pack file…";
        try
        {
            result = publish ? await Sharing.PublishAsync(Pack.Id, format == "wastickers") : await Sharing.SaveFileAsync(Pack.Id, format == "wastickers");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or JSException or OperationCanceledException)
        {
            var reference = Log.Error("Pack share/export", exception);
            error = exception is OperationCanceledException ? "Sharing timed out. Please try again." : exception.Message.Split('\n')[0];
            error += $" Reference: {reference}.";
        }
        finally
        {
            isWorking = false;
        }
    }

    private async Task CopyLink()
    {
        if (sharingClient is null || result?.Url is null) return;
        try
        {
            var copied = await sharingClient.InvokeAsync<bool>("copyText", result.Url);
            copyNotice = copied ? "Pack link copied." : "Select and copy the pack link above.";
        }
        catch (JSException)
        {
            copyNotice = "Select and copy the pack link above.";
        }
    }

    private Task Close() => isWorking ? Task.CompletedTask : OnClose.InvokeAsync();
    private Task HandleKey(KeyboardEventArgs args) => args.Key == "Escape" ? Close() : Task.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (sharingClient is not null) await sharingClient.DisposeAsync();
            if (dialogHelper is not null) await dialogHelper.DisposeAsync();
        }
        catch (JSDisconnectedException) { }
    }
}
