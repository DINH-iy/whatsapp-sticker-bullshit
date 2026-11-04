using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.RateLimiting;
using StickerApp.Core;
using StickerApp.ShareServer;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = PackArchive.MaxArchiveBytes);
builder.Services.Configure<PinterestOptions>(builder.Configuration.GetSection("Pinterest"));
builder.Services.AddHttpClient();
builder.Services.AddSingleton<PinterestService>();

var publishingToken = builder.Configuration["Sharing:PublishToken"];
if (string.IsNullOrWhiteSpace(publishingToken) || publishingToken.Length is < 32 or > 512)
    throw new InvalidOperationException("Set Sharing__PublishToken to a random token of 32–512 characters.");
var expectedTokenHash = SHA256.HashData(Encoding.UTF8.GetBytes(publishingToken));

builder.Services.AddSingleton<PackRepository>();
builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy
    .AllowAnyOrigin().WithMethods("GET", "POST", "DELETE").AllowAnyHeader()));
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddConcurrencyLimiter("uploads", limiter =>
    {
        limiter.PermitLimit = 2;
        limiter.QueueLimit = 0;
    });
});

var app = builder.Build();
app.UseCors();
app.UseRateLimiter();
app.Use(async (context, next) =>
{
    context.Response.Headers.XContentTypeOptions = "nosniff";
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

app.MapGet("/api/pinterest/login", (PinterestService pinterest) =>
{
    var url = pinterest.CreateAuthorizationUrl(out var state);
    return Results.Redirect(url);
});

app.MapGet("/api/pinterest/callback", async (string? code, string? state, PinterestService pinterest, CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(state))
        return Results.BadRequest(new { error = "Pinterest did not return an authorization code." });
    try
    {
        var session = await pinterest.CompleteAuthorizationAsync(code, state, cancellationToken);
        return Results.Redirect($"{pinterest.PostLoginRedirect}?session={Uri.EscapeDataString(session)}");
    }
    catch (InvalidOperationException exception)
    {
        return Results.BadRequest(new { error = exception.Message });
    }
});

app.MapGet("/api/pinterest/boards", async (HttpRequest request, PinterestService pinterest, CancellationToken cancellationToken) =>
{
    var session = request.Headers["X-Pinterest-Session"].ToString();
    if (string.IsNullOrWhiteSpace(session))
        return Results.Unauthorized();
    try { return Results.Json(await pinterest.GetBoardsAsync(session, cancellationToken)); }
    catch (UnauthorizedAccessException) { return Results.Unauthorized(); }
    catch (InvalidOperationException exception) { return Results.BadGateway(new { error = exception.Message }); }
});

app.MapGet("/api/pinterest/boards/{boardId}/pins", async (string boardId, HttpRequest request, PinterestService pinterest, CancellationToken cancellationToken) =>
{
    var session = request.Headers["X-Pinterest-Session"].ToString();
    if (string.IsNullOrWhiteSpace(session))
        return Results.Unauthorized();
    try { return Results.Json(await pinterest.GetPinsAsync(session, boardId, cancellationToken)); }
    catch (UnauthorizedAccessException) { return Results.Unauthorized(); }
    catch (InvalidOperationException exception) { return Results.BadGateway(new { error = exception.Message }); }
});

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
});

app.Run();
