using Microsoft.AspNetCore.Components.Forms;
using StickerApp.Core;

namespace StickerApp.Shared.Pages;

public partial class Packs
{
    private StickerLibrary library = new();
    private readonly Dictionary<Guid, string> covers = [];
    private string? error;
    private bool busy;
    private bool showCreate;

    protected override Task OnInitializedAsync() => Run(Reload);

    private async Task Reload()
    {
        library = await Store.ReadAsync();
        covers.Clear();
        foreach (var pack in library.Packs)
        {
            if (pack.CoverImage is not null)
                covers[pack.Id] = pack.CoverImage;
            else if (library.Stickers.FirstOrDefault(image => image.Id == pack.StickerIds.FirstOrDefault()) is { } first)
                covers[pack.Id] = await Store.PreviewAsync(first);
        }
    }

    private void PackCreated(Guid id)
    {
        showCreate = false;
        Navigation.NavigateTo($"packs/{id}");
    }

    private Task ImportPack(InputFileChangeEventArgs args) => Run(async () =>
    {
        await using var input = args.File.OpenReadStream(PackArchive.MaxArchiveBytes);
        var id = await Store.ImportPackAsync(input);
        Navigation.NavigateTo($"packs/{id}");
    });

    private async Task Run(Func<Task> action)
    {
        if (busy) return;
        busy = true;
        error = null;
        try { await action(); }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            error = exception.Message;
        }
        finally { busy = false; }
    }
}
