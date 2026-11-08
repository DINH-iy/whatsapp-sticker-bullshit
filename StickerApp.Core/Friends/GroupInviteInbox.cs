using System.Text.RegularExpressions;

namespace StickerApp.Core;

public sealed record GroupInvitation(string ServerUrl,string Code)
{
    public static GroupInvitation? Parse(string? value)
    {
        if(value is null||value.Length>4096||!Uri.TryCreate(value,UriKind.Absolute,out var uri))return null;
        if(!string.IsNullOrEmpty(uri.UserInfo))return null;
        var native=uri.Scheme=="stikerin"&&uri.Host=="join";
        if(!native&&(!(uri.Scheme=="https"||uri.Scheme=="http"&&uri.IsLoopback)||!(uri.AbsolutePath=="/invite"||uri.AbsolutePath.StartsWith("/invite/",StringComparison.Ordinal))))return null;
        try
        {
            var parts=uri.Fragment.TrimStart('#').Split('&',StringSplitOptions.RemoveEmptyEntries).Select(part=>part.Split('=',2)).ToArray();
            if(parts.Any(part=>part.Length!=2)||parts.Select(part=>part[0]).Distinct().Count()!=parts.Length)return null;
            var values=parts.ToDictionary(part=>part[0],part=>Uri.UnescapeDataString(part[1]));
            if(!values.TryGetValue("code",out var code)||!Regex.IsMatch(code,"^[a-zA-Z0-9][a-zA-Z0-9-]{15,127}$"))return null;
            var server=native?values.GetValueOrDefault("server"):uri.GetLeftPart(UriPartial.Authority);
            if(!Uri.TryCreate(server,UriKind.Absolute,out var endpoint)||!(endpoint.Scheme=="https"||endpoint.Scheme=="http"&&endpoint.IsLoopback)||!string.IsNullOrEmpty(endpoint.UserInfo)||endpoint.AbsolutePath!="/"||endpoint.Query!=""||endpoint.Fragment!="")return null;
            return new(endpoint.GetLeftPart(UriPartial.Authority),code);
        }
        catch(ArgumentException){return null;}
        catch(UriFormatException){return null;}
    }
}

// Native URL handlers queue an invitation; joining always needs a separate user confirmation.
public static class GroupInviteInbox
{
    private static readonly object Gate=new();
    private static GroupInvitation? pending;
    public static event Action? Changed;
    public static bool Receive(string? value)
    {
        var invite=GroupInvitation.Parse(value);if(invite is null)return false;
        lock(Gate)pending=invite;Changed?.Invoke();return true;
    }
    public static GroupInvitation? Peek(){lock(Gate)return pending;}
    public static GroupInvitation? Take(){lock(Gate){var value=pending;pending=null;return value;}}
}
