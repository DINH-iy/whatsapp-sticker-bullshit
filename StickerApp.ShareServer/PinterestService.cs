using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace StickerApp.ShareServer;

public sealed class PinterestService(IHttpClientFactory clients, IOptions<PinterestOptions> options)
{
    private const string AuthorizeUrl = "https://www.pinterest.com/oauth/";
    private const string ApiBaseUrl = "https://api.pinterest.com/v5";
    private static readonly string[] Scopes = ["boards:read", "pins:read"];
    private readonly ConcurrentDictionary<string, Session> sessions = new();
    private readonly ConcurrentDictionary<string, DateTimeOffset> pendingStates = new();
    private readonly PinterestOptions settings = options.Value;

    public string CreateAuthorizationUrl(out string state)
    {
        if (string.IsNullOrWhiteSpace(settings.ClientId) || string.IsNullOrWhiteSpace(settings.RedirectUri))
            throw new InvalidOperationException("Pinterest OAuth is not configured.");
        state = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var query = new Dictionary<string, string>
        {
            ["response_type"] = "code",
            ["client_id"] = settings.ClientId,
            ["redirect_uri"] = settings.RedirectUri,
            ["scope"] = string.Join(",", Scopes),
            ["state"] = state
        };
        pendingStates[state] = DateTimeOffset.UtcNow.AddMinutes(10);
        return $"{AuthorizeUrl}?{QueryString.Create(query)}";
    }

    public async Task<string> CompleteAuthorizationAsync(string code, string state, CancellationToken cancellationToken)
    {
        if (!pendingStates.TryRemove(state, out var expiresAt) || expiresAt <= DateTimeOffset.UtcNow)
            throw new InvalidOperationException("The Pinterest OAuth state is invalid or expired.");
        if (string.IsNullOrWhiteSpace(settings.ClientSecret))
            throw new InvalidOperationException("Pinterest client secret is not configured on the server.");
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.pinterest.com/v5/oauth/token")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["code"] = code,
                ["redirect_uri"] = settings.RedirectUri
            })
        };
        var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{settings.ClientId}:{settings.ClientSecret}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);
        var response = await clients.CreateClient().SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException("Pinterest rejected the OAuth code.");
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        var token = document.RootElement.GetProperty("access_token").GetString();
        if (string.IsNullOrWhiteSpace(token))
            throw new InvalidOperationException("Pinterest returned no access token.");
        sessions[state] = new Session(token, DateTimeOffset.UtcNow.AddHours(1));
        return state;
    }

    public async Task<JsonDocument> GetBoardsAsync(string session, CancellationToken cancellationToken) =>
        await GetAsync(session, "/boards?bookmark=", cancellationToken);

    public async Task<JsonDocument> GetPinsAsync(string session, string boardId, CancellationToken cancellationToken) =>
        await GetAsync(session, $"/boards/{Uri.EscapeDataString(boardId)}/pins?bookmark=", cancellationToken);

    public string PostLoginRedirect => settings.PostLoginRedirect;

    private async Task<JsonDocument> GetAsync(string sessionId, string path, CancellationToken cancellationToken)
    {
        if (!sessions.TryGetValue(sessionId, out var session) || session.ExpiresAt <= DateTimeOffset.UtcNow)
            throw new UnauthorizedAccessException("Pinterest authorization has expired.");
        using var request = new HttpRequestMessage(HttpMethod.Get, ApiBaseUrl + path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);
        var response = await clients.CreateClient().SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException("Pinterest could not return the requested data.");
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
    }

    private sealed record Session(string AccessToken, DateTimeOffset ExpiresAt);
}
