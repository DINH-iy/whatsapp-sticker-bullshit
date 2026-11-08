namespace StickerApp.ShareServer.Friends;

public static class InvitationEndpoints
{
    public sealed record InviteRequest(string? Label,int Hours=24,int Uses=1);
    public sealed record PreviewRequest(string? InviteCode);
    public static void MapInvitations(this WebApplication app)
    {
        app.MapGet("/api/friends/invitations",async(HttpRequest request,FriendsRepository repo)=>
        {
            var member=await FriendsEndpoints.Member(request,repo);return member is null?Results.Unauthorized():Results.Ok(await repo.Invitations(member));
        });
        app.MapPost("/api/friends/invitations",async(HttpRequest request,FriendsRepository repo)=>
        {
            var member=await FriendsEndpoints.Member(request,repo);if(member is null)return Results.Unauthorized();
            try{var data=await CollectionEndpoints.SmallBody<InviteRequest>(request);if(data is null)return Results.BadRequest();return Results.Ok(await repo.CreateInvitation(member,data.Label,data.Hours,data.Uses));}
            catch(UnauthorizedAccessException){return Results.Unauthorized();}
            catch(Exception e)when(e is InvalidDataException or System.Text.Json.JsonException){return Results.BadRequest(new{error=e.Message});}
        }).RequireRateLimiting("friends-invitations");
        app.MapDelete("/api/friends/invitations/{id:guid}",async(Guid id,HttpRequest request,FriendsRepository repo)=>
        {
            var member=await FriendsEndpoints.Member(request,repo);if(member is null)return Results.Unauthorized();return await repo.RevokeInvitation(member,id)?Results.NoContent():Results.NotFound();
        });
        app.MapPost("/api/friends/invite-preview",async(HttpRequest request,FriendsRepository repo)=>
        {
            try{var data=await CollectionEndpoints.SmallBody<PreviewRequest>(request);if(data?.InviteCode is null)return Results.BadRequest();var preview=await repo.PreviewInvitation(data.InviteCode);return preview is null?Results.NotFound():Results.Ok(preview);}
            catch(Exception e)when(e is InvalidDataException or System.Text.Json.JsonException){return Results.BadRequest();}
        }).RequireRateLimiting("friends-preview");
        app.MapGet("/invite/{label?}",(HttpContext context,IConfiguration configuration)=>
        {
            var nonce=Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(16));
            context.Response.Headers.CacheControl="no-store";context.Response.Headers["Referrer-Policy"]="no-referrer";
            context.Response.Headers["Content-Security-Policy"]=$"default-src 'none'; script-src 'nonce-{nonce}'; style-src 'nonce-{nonce}'; base-uri 'none'; frame-ancestors 'none'";
            return Results.Content(InviteLandingPage.Render(nonce,configuration["Friends:BrowserAppUrl"]),"text/html; charset=utf-8");
        });
    }
}
