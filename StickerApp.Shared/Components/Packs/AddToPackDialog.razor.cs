using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using StickerApp.Core;

namespace StickerApp.Shared.Components.Packs;

public partial class AddToPackDialog
{
    [Parameter, EditorRequired] public Sticker Image { get; set; } = default!;
    [Parameter] public EventCallback<string> OnAdded { get; set; }
    [Parameter] public EventCallback OnCancel { get; set; }

    private ElementReference dialog;
    private IJSObjectReference? dialogHelper;
    private List<StickerPack> availablePacks = [];
    private string newPackName = "";
    private string newPackPublisher = "";
    private string? error;
    private bool isLoading = true;
    private bool isSaving;

    protected override async Task OnInitializedAsync()
    {
        try
        {
            var library = await Store.ReadAsync();
            availablePacks = library.Packs.ToList();
        }
        catch (Exception exception) when (IsStorageError(exception))
        {
            error = exception.Message;
        }
        finally
        {
            isLoading = false;
        }
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender) return;
        dialogHelper = await JS.InvokeAsync<IJSObjectReference>("import", "./_content/StickerApp.Shared/js/ui/dialog.js");
        await dialogHelper.InvokeVoidAsync("open", dialog);
    }

    private Task AddToExistingPack(StickerPack pack) => SaveAddition(
        () => Store.AddToPackAsync(pack.Id, [Image.Id]), pack.Name);

    private Task CreatePackAndAdd() => SaveAddition(
        () => Store.CreatePackWithStickerAsync(newPackName, Image.Id, newPackPublisher), newPackName.Trim());

    private async Task SaveAddition(Func<Task> save, string packName)
    {
        if (isSaving) return;
        isSaving = true;
        error = null;

        try
        {
            await save();
            await OnAdded.InvokeAsync($"Added “{Image.Name}” to “{packName}”.");
        }
        catch (Exception exception) when (IsStorageError(exception))
        {
            error = exception.Message;
        }
        finally
        {
            isSaving = false;
        }
    }

    private static bool IsStorageError(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or InvalidOperationException or System.Text.Json.JsonException;

    private Task Cancel() => OnCancel.InvokeAsync();
    private Task HandleKey(KeyboardEventArgs args) => args.Key == "Escape" ? Cancel() : Task.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        if (dialogHelper is null) return;

        try
        {
            await dialogHelper.DisposeAsync();
        }
        catch (JSDisconnectedException)
        {
            // Closing the app can disconnect JavaScript before this component is disposed.
        }
    }
}
