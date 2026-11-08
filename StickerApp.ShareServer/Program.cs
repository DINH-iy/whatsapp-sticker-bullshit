using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;
using StickerApp.Core;
using StickerApp.ShareServer;
using StickerApp.ShareServer.Friends;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = PackArchive.MaxArchiveBytes);

var publishingToken = builder.Configuration["Sharing:PublishToken"];
if (string.IsNullOrWhiteSpace(publishingToken) || publishingToken.Length is < 32 or > 512)
    throw new InvalidOperationException("Set Sharing__PublishToken to a random token of 32–512 characters.");
var expectedTokenHash = SHA256.HashData(Encoding.UTF8.GetBytes(publishingToken));

builder.Services.AddRequestTimeouts(options => options.DefaultPolicy = new Microsoft.AspNetCore.Http.Timeouts.RequestTimeoutPolicy { Timeout = TimeSpan.FromMinutes(3), TimeoutStatusCode = 408 });
builder.Services.AddSingleton<PackRepository>();
builder.Services.AddSingleton<FriendsRepository>();
builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy
    .AllowAnyOrigin().WithMethods("GET", "POST", "PUT", "DELETE").AllowAnyHeader()));
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    // This uses the socket peer, never an untrusted forwarding header. Put a per-client limit at the edge too.
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        RateLimitPartition.GetTokenBucketLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new()
        { TokenLimit = 120, TokensPerPeriod = 120, ReplenishmentPeriod = TimeSpan.FromMinutes(1), AutoReplenishment = true, QueueLimit = 0 }));
    options.AddConcurrencyLimiter("downloads", limiter => { limiter.PermitLimit = 2; limiter.QueueLimit = 0; });
    options.AddPolicy("friends-join", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new()
        { PermitLimit = 5, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    options.AddPolicy("friends-invitations", context => RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new() { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    options.AddPolicy("friends-preview", context => RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new() { PermitLimit = 15, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    options.AddConcurrencyLimiter("uploads", limiter =>
    {
        limiter.PermitLimit = 2;
        limiter.QueueLimit = 0;
    });
});

var app = builder.Build();
app.UseRouting();
app.UseRequestTimeouts();
app.UseCors();
app.UseRateLimiter();
app.Use(async (context, next) =>
{
    context.Response.Headers.XContentTypeOptions = "nosniff";
    if (context.Request.Path.StartsWithSegments("/api/friends")) context.Response.Headers.CacheControl = "private, no-store";
    context.Response.Headers["Referrer-Policy"] = "no-referrer";
    await next();
});

bool CanPublish(HttpRequest request)
{
    var authorization = request.Headers.Authorization.ToString();
    if (!authorization.StartsWith("Bearer ", StringComparison.Ordinal) || authorization.Length > 1024) return false;
    var suppliedHash = SHA256.HashData(Encoding.UTF8.GetBytes(authorization[7..]));
    return CryptographicOperations.FixedTimeEquals(suppliedHash, expectedTokenHash);
}

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.MapPost("/api/packs", async (HttpRequest request, PackRepository repository, CancellationToken cancellationToken) =>
{
    if (!CanPublish(request)) return Results.Unauthorized();
    if (request.ContentLength > PackArchive.MaxArchiveBytes) return Results.StatusCode(413);

    try
    {
        var pack = await repository.PublishAsync(request.Body, cancellationToken);
        return Results.Ok(new { path = $"/p/{pack.Sha256}", sha256 = pack.Sha256 });
    }
    catch (SharingStorageFullException)
    {
        return Results.StatusCode(507);
    }
    catch (Exception exception) when (exception is InvalidDataException or System.Text.Json.JsonException)
    {
        return Results.BadRequest(new { error = "This file is not a valid sticker pack." });
    }
}).RequireRateLimiting("uploads");

app.MapDelete("/api/packs/{hash}", async (string hash, HttpRequest request, PackRepository repository) =>
{
    if (!CanPublish(request)) return Results.Unauthorized();
    if (!PackRepository.IsValidHash(hash)) return Results.NotFound();
    await repository.DeleteAsync(hash);
    return Results.NoContent();
});

app.MapGet("/p/{hash}", async (string hash, HttpContext context, PackRepository repository) =>
{
    var pack = await repository.FindAsync(hash);
    if (pack is null) return Results.NotFound();
    context.Response.Headers.CacheControl = "no-store";
    context.Response.Headers["Content-Security-Policy"] = "default-src 'none'; style-src 'unsafe-inline'; frame-ancestors 'none'; base-uri 'none'; form-action 'none'";
    return Results.Content(PackLandingPage.Render(pack), "text/html; charset=utf-8");
});

app.MapGet("/p/{hash}/download", async (string hash, HttpContext context, PackRepository repository) =>
{
    var pack = await repository.FindAsync(hash);
    if (pack is null) return Results.NotFound();
    context.Response.Headers.CacheControl = "no-store";
    return Results.File(repository.ArchivePath(hash), "application/zip", pack.FileName, enableRangeProcessing: true);
}).RequireRateLimiting("downloads");

app.MapFriends();
app.MapCollections();
app.MapInvitations();
app.Run();
