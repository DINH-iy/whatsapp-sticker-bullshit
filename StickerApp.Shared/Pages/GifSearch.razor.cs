using Microsoft.JSInterop;

namespace StickerApp.Shared.Pages;

public partial class GifSearch
{
    [Microsoft.AspNetCore.Components.Parameter] public bool Embedded { get; set; }
    private readonly List<GifResult> results = [];
    private IJSObjectReference? client;
    private string query = "";
    private string activeQuery = "";
    private string? error;
    private string? shareNotice;
    private GifResult? selectedGif;
    private int currentPage;
    private bool isReady;
    private bool hasSearched;
    private bool isConfigured;
    private bool isLoading;
    private bool isCopying;
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

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender)
            return;

        try
        {
            client = await JS.InvokeAsync<IJSObjectReference>("import", "./_content/StickerApp.Shared/klipy-client.js");
            isConfigured = await client.InvokeAsync<bool>("hasApiKey");
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
        if (isLoading || string.IsNullOrWhiteSpace(query))
            return;

        hasSearched = true;
        activeQuery = query.Trim();
        currentPage = 0;
        results.Clear();
        selectedGif = null;
        shareNotice = null;
        hasNextPage = false;
        await FetchPage(1);
    }

    private Task LoadMore() => FetchPage(currentPage + 1);
    private Task RetrySearch() => FetchPage(currentPage + 1);

    private async Task FetchPage(int page)
    {
        if (isLoading || client is null)
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

    private async Task CopyLink(GifResult gif)
    {
        if (client is null || isCopying)
            return;

        isCopying = true;
        selectedGif = gif;

        try
        {
            var copied = await client.InvokeAsync<bool>("copyLink", gif.ShareUrl);
            shareNotice = copied ? "Link copied. Paste it into your chat." : "Select and copy the link above to share this GIF.";
            if (copied)
                await client.InvokeVoidAsync("registerShare", gif.Slug, activeQuery);
        }
        catch (JSException)
        {
            shareNotice = "Select and copy the link above to share this GIF.";
        }
        finally
        {
            isCopying = false;
        }
    }

    public async ValueTask DisposeAsync()
    {
        isDisposed = true;
        if (client is null)
            return;

        try
        {
            await client.InvokeVoidAsync("cancelSearch");
            await client.DisposeAsync();
        }
        catch (JSDisconnectedException)
        {
            // Closing a browser tab can disconnect JavaScript before disposal finishes.
        }
    }
}
