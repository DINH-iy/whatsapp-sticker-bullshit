using Microsoft.JSInterop;
using StickerApp.Shared.Services;

namespace StickerApp.Web.Services;

public sealed class BrowserPackFileSaver(IJSRuntime js) : IPackFileSaver
{
    public string ActionLabel => "Export pack file";

    public async Task SaveAsync(string fileName, Stream content, string contentType = "application/zip")
    {
        await using var module = await js.InvokeAsync<IJSObjectReference>("import", "./_content/StickerApp.Shared/pack-sharing.js");
        using var reference = new DotNetStreamReference(content, leaveOpen: true);
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        await module.InvokeVoidAsync("download", timeout.Token, fileName, reference, contentType);
    }
}
