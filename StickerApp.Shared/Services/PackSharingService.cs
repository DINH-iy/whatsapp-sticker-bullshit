using Microsoft.JSInterop;
using StickerApp.Core;

namespace StickerApp.Shared.Services;

/// <summary>Builds one portable pack, then either saves it locally or publishes it.</summary>
public sealed class PackSharingService(LibraryStore store, IPackFileSaver fileSaver, IJSRuntime js)
{
    public string FileActionLabel => fileSaver.ActionLabel;
    public sealed record ShareResult(string FileName, string Sha256, string? Url = null);

    public Task<ShareResult> SaveFileAsync(Guid packId) => WithArchiveAsync(packId, async (info, stream) =>
    {
        await fileSaver.SaveAsync(info.FileName, stream);
        return new ShareResult(info.FileName, info.Sha256);
    });

    public Task<ShareResult> PublishAsync(Guid packId) => WithArchiveAsync(packId, async (info, stream) =>
    {
        await using var module = await js.InvokeAsync<IJSObjectReference>("import", "./_content/StickerApp.Shared/pack-sharing.js");
        using var reference = new DotNetStreamReference(stream, leaveOpen: true);
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        var result = await module.InvokeAsync<PublishedLink>("publish", timeout.Token, reference);
        if (!string.Equals(result.Sha256, info.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The server returned a different pack checksum. The link was not accepted.");
        return new ShareResult(info.FileName, info.Sha256, result.Url);
    });

    public sealed class PublishedLink
    {
        public string Url { get; set; } = "";
        public string Sha256 { get; set; } = "";
    }

    private async Task<ShareResult> WithArchiveAsync(Guid packId, Func<PackArchive.ExportInfo, Stream, Task<ShareResult>> action)
    {
        var path = Path.Combine(Path.GetTempPath(), $"sticker-export-{Guid.NewGuid():N}.zip");
        await using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite,
            FileShare.None, 64 * 1024, FileOptions.Asynchronous | FileOptions.DeleteOnClose);
        var info = await store.ExportPackAsync(packId, stream);
        return await action(info, stream);
    }
}
