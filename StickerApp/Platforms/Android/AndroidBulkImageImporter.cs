using Android.Content;
using Android.Provider;
using StickerApp.Shared.Services;
using Uri = Android.Net.Uri;

namespace StickerApp.Services;

public sealed class AndroidBulkImageImporter : IBulkImageImporter
{
    public bool IsAvailable => true;
    public string ActionLabel => "Import folder";

    public async Task<IReadOnlyList<BulkImageFile>> PickFolderAsync()
    {
        var uri = await MainActivity.PickFolderAsync();
        var resolver = global::Android.App.Application.Context.ContentResolver
            ?? throw new InvalidOperationException("Android content services are unavailable.");
        var files = new List<BulkImageFile>();
        await ReadFolderAsync(resolver, uri, files);
        return files;
    }

    private static async Task ReadFolderAsync(ContentResolver resolver, Uri treeUri, List<BulkImageFile> files)
    {
        var documentId = DocumentsContract.GetTreeDocumentId(treeUri)
            ?? throw new InvalidOperationException("The selected folder could not be opened.");
        await ReadChildrenAsync(resolver, treeUri, documentId, files);
    }

    private static async Task ReadChildrenAsync(ContentResolver resolver, Uri treeUri, string documentId, List<BulkImageFile> files)
    {
        var childrenUri = DocumentsContract.BuildChildDocumentsUriUsingTree(treeUri, documentId);
        using var cursor = resolver.Query(childrenUri, ["document_id", "display_name", "mime_type", "flags"], null, null, null)
            ?? throw new IOException("The selected folder could not be read.");
        var idIndex = cursor.GetColumnIndex("document_id");
        var nameIndex = cursor.GetColumnIndex("display_name");
        var mimeIndex = cursor.GetColumnIndex("mime_type");
        var flagsIndex = cursor.GetColumnIndex("flags");
        while (cursor.MoveToNext())
        {
            var childId = cursor.GetString(idIndex) ?? "";
            var name = cursor.GetString(nameIndex) ?? childId;
            var mime = cursor.GetString(mimeIndex) ?? "";
            var flags = cursor.GetInt(flagsIndex);
            var childUri = DocumentsContract.BuildDocumentUriUsingTree(treeUri, childId);
            if (mime == "vnd.android.document/directory")
            {
                await ReadChildrenAsync(resolver, treeUri, childId, files);
                continue;
            }
            if (!IsSupportedImage(name, mime))
                continue;
            await using var input = resolver.OpenInputStream(childUri)
                ?? throw new IOException($"Could not read {name}.");
            using var output = new MemoryStream();
            await input.CopyToAsync(output);
            if (output.Length > Core.LibraryStore.MaxImportBytes)
                continue;
            files.Add(new BulkImageFile(name, output.ToArray()));
        }
    }

    private static bool IsSupportedImage(string name, string mime) =>
        mime is "image/jpeg" or "image/png" or "image/gif" or "image/webp" ||
        Path.GetExtension(name).ToLowerInvariant() is ".jpg" or ".jpeg" or ".png" or ".gif" or ".webp";
}
