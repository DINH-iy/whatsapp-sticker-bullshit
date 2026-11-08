using StickerApp.Core;

namespace StickerApp.ShareServer.Friends;

public static class FriendsEndpoints
{
    public sealed record JoinRequest(string? InviteCode, string? Username);
    internal static Task<FriendsRepository.Member?> Member(HttpRequest request, FriendsRepository repository)
    {
        var authorization = request.Headers.Authorization.ToString();
        return repository.Authenticate(authorization.StartsWith("Bearer ", StringComparison.Ordinal) ? authorization[7..] : null);
    }
    public static void MapFriends(this WebApplication app)
    {
        app.MapGet("/api/friends/status", (FriendsRepository repo) => Results.Ok(new { enabled = repo.Enabled, visibility = "invited-friends-only" }));
        app.MapPost("/api/friends/join", async (HttpRequest request, FriendsRepository repo) =>
        {
            if (!repo.Enabled) return Results.NotFound();
            if (request.ContentLength > 4096) return Results.BadRequest();
            try
            {
                using var body = new MemoryStream();
                var buffer = new byte[4097]; int count;
                while ((count = await request.Body.ReadAsync(buffer, request.HttpContext.RequestAborted)) > 0)
                {
                    if (body.Length + count > 4096) return Results.BadRequest();
                    await body.WriteAsync(buffer.AsMemory(0, count));
                }
                var data = System.Text.Json.JsonSerializer.Deserialize<JoinRequest>(body.ToArray(), new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
                if (data?.InviteCode is null || data.Username is null) return Results.BadRequest();
                var joined = await repo.Join(data.InviteCode, data.Username);
                return joined is null ? Results.Unauthorized() : Results.Ok(joined);
            }
            catch (Exception e) when (e is InvalidDataException or System.Text.Json.JsonException) { return Results.BadRequest(new { error = e.Message }); }
        }).RequireRateLimiting("friends-join");
        app.MapDelete("/api/friends/membership", async (HttpRequest request, FriendsRepository repo) =>
        {
            var member = await Member(request, repo); if (member is null) return Results.Unauthorized();
            await repo.Leave(member); return Results.NoContent();
        });
        app.MapGet("/api/friends/packs", async (HttpContext context, FriendsRepository repo, int offset = 0) =>
        {
            var member = await Member(context.Request, repo); if (member is null) return Results.Unauthorized();
            context.Response.Headers.CacheControl = "private, no-store";
            var list = await repo.List(offset); return Results.Ok(new { items = list.Items, total = list.Total, memberId = member.Id, username = member.Username });
        });
        app.MapPut("/api/friends/packs/{id:guid}", async (Guid id, HttpContext context, FriendsRepository repo) =>
        {
            var member = await Member(context.Request, repo); if (member is null) return Results.Unauthorized();
            if (context.Request.ContentLength > PackArchive.MaxArchiveBytes) return Results.StatusCode(413);
            if (!await repo.ReserveTransfer(member, context.Request.ContentLength ?? PackArchive.MaxArchiveBytes))
                return Results.Json(new { error = "The group or device daily transfer limit has been reached. Try again tomorrow." }, statusCode: 429);
            try { await repo.Publish(member, id, context.Request.Body, context.RequestAborted); return Results.NoContent(); }
            catch (UnauthorizedAccessException) { return Results.Unauthorized(); }
            catch (SharingStorageFullException) { return Results.StatusCode(507); }
            catch (Exception e) when (e is InvalidDataException or System.Text.Json.JsonException) { return Results.BadRequest(new { error = e.Message }); }
        }).RequireRateLimiting("uploads");
        app.MapDelete("/api/friends/packs/{id:guid}", async (Guid id, HttpContext context, FriendsRepository repo) =>
        {
            var member = await Member(context.Request, repo); if (member is null) return Results.Unauthorized();
            await repo.Remove(member, id); return Results.NoContent();
        });
        app.MapGet("/api/friends/packs/{owner:guid}/{id:guid}/download", async (Guid owner, Guid id, HttpContext context, FriendsRepository repo) =>
        {
            var member = await Member(context.Request, repo); if (member is null) return Results.Unauthorized();
            var pack = await repo.Open(owner, id); if (pack is null) return Results.NotFound();
            context.Response.Headers.CacheControl = "private, no-store";
            if (!await repo.ReserveTransfer(member, pack.Value.Stream.Length))
            {
                await pack.Value.Stream.DisposeAsync();
                return Results.Json(new { error = "The group or device daily transfer limit has been reached. Try again tomorrow." }, statusCode: 429);
            }
            return Results.Stream(pack.Value.Stream, "application/zip", pack.Value.Name);
        }).RequireRateLimiting("downloads");
        app.MapGet("/api/friends/packs/{owner:guid}/{id:guid}/preview", async (Guid owner, Guid id, HttpContext context, FriendsRepository repo) =>
        {
            var member = await Member(context.Request, repo); if (member is null) return Results.Unauthorized();
            var pack = await repo.Open(owner, id); if (pack is null) return Results.NotFound();
            await using var stream = pack.Value.Stream;
            using var archive = new System.IO.Compression.ZipArchive(stream, System.IO.Compression.ZipArchiveMode.Read);
            var first = archive.Entries.FirstOrDefault(entry => entry.FullName.StartsWith("media/00.", StringComparison.Ordinal));
            if (first is null || first.Length > LibraryStore.MaxImportBytes) return Results.NotFound();
            if (!await repo.ReserveTransfer(member, first.Length))
                return Results.Json(new { error = "The group or device daily transfer limit has been reached. Try again tomorrow." }, statusCode: 429);
            await using var input = first.Open();
            using var output = new MemoryStream();
            await input.CopyToAsync(output, context.RequestAborted);
            var bytes = output.ToArray();
            var info = MediaInspector.Inspect(bytes);
            context.Response.Headers.CacheControl = "private, no-store";
            return Results.File(bytes, info.MimeType);
        }).RequireRateLimiting("downloads");
    }
}
