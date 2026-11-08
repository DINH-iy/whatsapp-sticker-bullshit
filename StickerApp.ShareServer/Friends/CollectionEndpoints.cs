using System.IO.Compression;
using System.Text.Json;
using StickerApp.Core;

namespace StickerApp.ShareServer.Friends;

public static class CollectionEndpoints
{
    public sealed record NewCollection(string? Name);
    public sealed record Reaction(string? Emoji);
    internal static async Task<T?> SmallBody<T>(HttpRequest request)
    {
        using var output=new MemoryStream();var buffer=new byte[4097];int count;
        while((count=await request.Body.ReadAsync(buffer,request.HttpContext.RequestAborted))>0)
        {if(output.Length+count>4096)throw new InvalidDataException("Request exceeds 4 KB.");await output.WriteAsync(buffer.AsMemory(0,count));}
        return JsonSerializer.Deserialize<T>(output.ToArray(),new JsonSerializerOptions(JsonSerializerDefaults.Web));
    }
    public static void MapCollections(this WebApplication app)
    {
        app.MapGet("/api/friends/collections",async(HttpRequest request,FriendsRepository repo)=>
        {
            if(await FriendsEndpoints.Member(request,repo) is null)return Results.Unauthorized();
            return Results.Ok(await repo.Collections());
        });
        app.MapPost("/api/friends/collections",async(HttpRequest request,FriendsRepository repo)=>
        {
            var member=await FriendsEndpoints.Member(request,repo);if(member is null)return Results.Unauthorized();
            try{var data=await SmallBody<NewCollection>(request);if(data?.Name is null)return Results.BadRequest();return Results.Ok(await repo.CreateCollection(member,data.Name));}
            catch(UnauthorizedAccessException){return Results.Unauthorized();}
            catch(SharingStorageFullException){return Results.StatusCode(507);}
            catch(Exception e)when(e is InvalidDataException or JsonException){return Results.BadRequest(new{error=e.Message});}
        }).RequireRateLimiting("uploads");
        app.MapGet("/api/friends/collections/{id:guid}",async(Guid id,HttpRequest request,FriendsRepository repo)=>
        {
            var member=await FriendsEndpoints.Member(request,repo);if(member is null)return Results.Unauthorized();
            var collection=await repo.Collection(id,member);return collection is null?Results.NotFound():Results.Ok(collection);
        });
        app.MapPut("/api/friends/collections/{id:guid}",async(Guid id,HttpRequest request,FriendsRepository repo)=>
        {
            var member=await FriendsEndpoints.Member(request,repo);if(member is null)return Results.Unauthorized();
            if(request.ContentLength>PackArchive.MaxArchiveBytes)return Results.StatusCode(413);
            if(!await repo.ReserveTransfer(member,request.ContentLength??PackArchive.MaxArchiveBytes))return Results.Json(new{error="Daily transfer limit reached. Try again tomorrow."},statusCode:429);
            if(!int.TryParse(request.Headers.IfMatch.ToString().Trim('"'),out var revision)||revision<1)return Results.BadRequest(new{error="The collection revision is required."});
            try{return Results.Ok(await repo.UpdateCollection(member,id,revision,request.Body,request.HttpContext.RequestAborted));}
            catch(CollectionConflictException e){return Results.Conflict(new{error=e.Message});}
            catch(UnauthorizedAccessException){return Results.Unauthorized();}
            catch(SharingStorageFullException){return Results.StatusCode(507);}
            catch(Exception e)when(e is InvalidDataException or JsonException){return Results.BadRequest(new{error=e.Message});}
        }).RequireRateLimiting("uploads");
        app.MapPost("/api/friends/collections/{id:guid}/stickers/{sticker:guid}/reaction",async(Guid id,Guid sticker,HttpRequest request,FriendsRepository repo)=>
        {
            var member=await FriendsEndpoints.Member(request,repo);if(member is null)return Results.Unauthorized();
            try{var reaction=await SmallBody<Reaction>(request);if(reaction?.Emoji is null)return Results.BadRequest();return await repo.React(member,id,sticker,reaction.Emoji)?Results.NoContent():Results.NotFound();}
            catch(UnauthorizedAccessException){return Results.Unauthorized();}
            catch(Exception e)when(e is InvalidDataException or JsonException){return Results.BadRequest(new{error=e.Message});}
        });
        app.MapGet("/api/friends/collections/{id:guid}/download",async(Guid id,int revision,HttpContext context,FriendsRepository repo)=>
        {
            var member=await FriendsEndpoints.Member(context.Request,repo);if(member is null)return Results.Unauthorized();
            try
            {
                var collection=await repo.OpenCollection(id,revision);if(collection is null)return Results.NotFound();
                if(!await repo.ReserveTransfer(member,collection.Value.Stream.Length)){await collection.Value.Stream.DisposeAsync();return Results.Json(new{error="Daily transfer limit reached. Try again tomorrow."},statusCode:429);}
                return Results.Stream(collection.Value.Stream,"application/zip",collection.Value.Name);
            }
            catch(CollectionConflictException e){return Results.Conflict(new{error=e.Message});}
        }).RequireRateLimiting("downloads");
        app.MapGet("/api/friends/collections/{id:guid}/stickers/{sticker:guid}/preview",async(Guid id,Guid sticker,HttpContext context,FriendsRepository repo)=>
        {
            var member=await FriendsEndpoints.Member(context.Request,repo);if(member is null)return Results.Unauthorized();
            var collection=await repo.OpenCollection(id,stickerId:sticker);if(collection is null)return Results.NotFound();
            await using var stream=collection.Value.Stream;using var zip=new ZipArchive(stream,ZipArchiveMode.Read);
            var entry=zip.GetEntry(collection.Value.Sticker!.File);if(entry is null||entry.Length>LibraryStore.MaxImportBytes)return Results.NotFound();
            if(!await repo.ReserveTransfer(member,entry.Length))return Results.Json(new{error="Daily transfer limit reached. Try again tomorrow."},statusCode:429);
            await using var input=entry.Open();using var output=new MemoryStream();await input.CopyToAsync(output,context.RequestAborted);
            return Results.File(output.ToArray(),collection.Value.Sticker.MimeType);
        }).RequireRateLimiting("downloads");
        app.MapDelete("/api/friends/collections/{id:guid}",async(Guid id,HttpRequest request,FriendsRepository repo)=>
        {
            var member=await FriendsEndpoints.Member(request,repo);if(member is null)return Results.Unauthorized();
            try{return await repo.RemoveCollection(member,id)?Results.NoContent():Results.NotFound();}
            catch(UnauthorizedAccessException){return Results.StatusCode(403);}
        });
    }
}
