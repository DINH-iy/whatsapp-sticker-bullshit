using Microsoft.JSInterop;
using Microsoft.AspNetCore.Components;
using StickerApp.Core;

namespace StickerApp.Shared.Pages.Gifs;

public partial class GifSearch
{
    [Parameter] public bool Embedded { get; set; }
    [Parameter] public Guid? PackId { get; set; }
    [Parameter] public EventCallback OnImported { get; set; }
    private IJSObjectReference? importSession;
    private HttpClient? nativeDownloadClient;
    private CancellationTokenSource? importCancellation;
    private readonly HashSet<string> importedUrls = [];
    private string? importingUrl, importNotice, importError;
    private bool importCommitting;
    private bool IsImporting => importCancellation is not null;
    private readonly List<GifResult> results = [];
    private IJSObjectReference? client;
    private string query = "";
    private string activeQuery = "";
    private string? error;
    private Sticker? importedImage;
    private int currentPage;
    private bool isReady;
    private bool hasSearched;
    private bool isConfigured;
    private bool isLoading;
    private bool hasNextPage;
    private bool isDisposed;

    // These small models describe the data returned by klipy-client.js, not the full provider response.
    public sealed class GifResult
    {
        public string Slug { get; set; } = "";
        public string Title { get; set; } = "";
        public string PreviewUrl { get; set; } = "";
        public string ShareUrl { get; set; } = "";
    }

    public sealed class GifSearchResponse
    {
        public List<GifResult> Items { get; set; } = [];
        public bool HasNextPage { get; set; }
    }

    protected override async Task OnAfterRenderInteropAsync(bool firstRender)
    {
        if (!firstRender)
            return;

        try
        {
            client = await JS.InvokeAsync<IJSObjectReference>("import", "./_content/StickerApp.Shared/js/imports/klipy/klipy-client.js");
            if (IsDisposed) return;
            isConfigured = await client.InvokeAsync<bool>("hasApiKey");
            if (IsDisposed) return;
            isReady = true;
        }
        catch (JSException)
        {
            error = "GIF search could not be loaded. Reload the app to try again.";
            isReady = true;
        }

        if (!isDisposed)
            StateHasChanged();
    }

    private async Task StartSearch()
    {
        if (isLoading || IsImporting || string.IsNullOrWhiteSpace(query))
            return;

        hasSearched = true;
        activeQuery = query.Trim();
        currentPage = 0;
        results.Clear();
        hasNextPage = false;
        await FetchPage(1);
    }

    private Task LoadMore() => FetchPage(currentPage + 1);
    private Task RetrySearch() => FetchPage(currentPage + 1);

    private async Task FetchPage(int page)
    {
        if (!Connection.Online) { error = "No internet connection. Reconnect to search for GIFs."; return; }
        if (isLoading || IsImporting || client is null)
            return;

        isLoading = true;
        error = null;
        StateHasChanged();

        try
        {
            var response = await client.InvokeAsync<GifSearchResponse>("search", activeQuery, page);
            if (isDisposed)
                return;

            results.AddRange(response.Items);
            currentPage = page;
            hasNextPage = response.HasNextPage;
        }
        catch (JSException exception)
        {
            Log.Error("GIF search", exception);
            // The JavaScript client supplies a safe message without request URLs or credentials.
            error = exception.Message.Split('\n')[0];
        }
        finally
        {
            isLoading = false;
        }
    }

    private async Task ImportAddedToPack(string message)
    {
        importedImage = null;
        importNotice = message;
        await OnImported.InvokeAsync();
    }

    private void KeepImportedInLibrary()
    {
        importedImage = null;
        importNotice = "GIF saved in My Library.";
    }

    private async Task ImportGif(GifResult gif)
    {
        if (client is null || IsImporting || isDisposed || importedUrls.Contains(gif.ShareUrl)) return;
        using var cancellation = Activity.BeginOperation(TimeSpan.FromSeconds(90));
        importCancellation = cancellation;
        importingUrl = gif.ShareUrl; importNotice = importError = null;
        try
        {
            var token = cancellation.Token;
            byte[] bytes;
            if (FormFactor.GetFormFactor() == "Web" || OperatingSystem.IsBrowser())
            {
                importSession ??= await client.InvokeAsync<IJSObjectReference>("createImportSession");
                await using var reference = await importSession.InvokeAsync<IJSStreamReference>("download", token, gif.ShareUrl, Store.GifImportLimitBytes);
                await using var input = await reference.OpenReadStreamAsync(Store.GifImportLimitBytes, token);
                using var memory = new MemoryStream();
                await input.CopyToAsync(memory, token);
                bytes = memory.ToArray();
            }
            else
            {
                nativeDownloadClient ??= new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false }) { Timeout = TimeSpan.FromSeconds(45) };
                bytes = await KlipyGifDownloadClient.DownloadAsync(nativeDownloadClient, gif.ShareUrl, token, Store.GifImportLimitBytes);
            }
            if (MediaInspector.Inspect(bytes).MimeType != "image/gif")
                throw new InvalidDataException("The downloaded file is not a valid GIF.");
            token.ThrowIfCancellationRequested();
            if (isDisposed) return;
            // The atomic library write finishes once started; cancellation only
            // applies before this point, never to a partially committed sticker.
            importCommitting = true;
            StateHasChanged();
            var stickerId = await Store.ImportAsync(ImportFileName(gif), bytes);
            importedUrls.Add(gif.ShareUrl);
            if (isDisposed) return;
            importedImage = (await Store.ReadAsync()).Stickers.Single(image => image.Id == stickerId);
            importNotice = "GIF imported into My Library.";
            await OnImported.InvokeAsync();
            await client.InvokeVoidAsync("registerShare", gif.Slug, activeQuery);
        }
        catch (OperationCanceledException) { if (!isDisposed) importNotice = "GIF import cancelled."; }
        // This is a UI event boundary: a failed import must not tear down the
        // renderer and leave the modal inert. InvalidDataException (including
        // the download size limit) does not derive from IOException.
        catch (Exception exception)
        {
            if (!isDisposed)
            {
                if (cancellation.IsCancellationRequested && !importCommitting) importNotice = "GIF import cancelled.";
                else
                {
                    Log.Error("KLIPY GIF import", exception);
                    importError = exception is HttpRequestException ? "Could not download this GIF. Check your connection and try again." : exception.Message.Split('\n')[0];
                }
            }
        }
        finally
        {
            importCancellation = null; importingUrl = null; importCommitting = false;
            if (!isDisposed) StateHasChanged();
        }
    }

    private static string ImportFileName(GifResult gif)
    {
        var name = new string(gif.Title.Where(c => !char.IsControl(c) && !"/\\:*?\"<>|".Contains(c)).Take(120).ToArray()).Trim().Trim('.');
        return (string.IsNullOrWhiteSpace(name) ? "KLIPY GIF" : name) + ".gif";
    }

    private async Task CancelImport()
    {
        if (importCommitting || importCancellation is null) return;
        importCancellation.Cancel();
        if (importSession is not null)
            try { await importSession.InvokeVoidAsync("cancel"); } catch (JSDisconnectedException) { }
    }

    protected override void OnRemoving()
    {
        isDisposed = true;
        if (!importCommitting) importCancellation?.Cancel();
    }
    protected override async ValueTask StopInteropAsync()
    {
        if (client is not null) await client.InvokeVoidAsync("cancelSearch");
    }
    protected override async ValueTask DisposeInteropAsync()
    {
        isDisposed = true;
        if (!importCommitting) importCancellation?.Cancel();
        nativeDownloadClient?.Dispose();
        if (client is null)
            return;

        try
        {
            if (importSession is not null)
            {
                await importSession.InvokeVoidAsync("dispose");
                await importSession.DisposeAsync();
            }
            await client.InvokeVoidAsync("cancelSearch");
            await client.DisposeAsync();
        }
        catch (JSDisconnectedException)
        {
            // Closing a browser tab can disconnect JavaScript before disposal finishes.
        }
    }
}
