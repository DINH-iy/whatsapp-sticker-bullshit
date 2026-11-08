using System.Text;
using System.Text.RegularExpressions;

namespace StickerApp.Shared.Services;

public sealed partial class AppLanguage
{
    private static string Normalize(string text)=>Regex.Replace(text.Trim(),@"\s+"," ");
    private static readonly Dictionary<string,string> Keys=Resources["en"].Keys.GroupBy(Normalize,StringComparer.Ordinal).ToDictionary(group=>group.Key,group=>group.First(),StringComparer.Ordinal);
    private sealed record Template(string Key,Regex Pattern,string[] Names);
    private static readonly Template[] Templates=Resources["en"].Keys.Where(key=>key.Contains('{')).Select(CreateTemplate).OfType<Template>().OrderByDescending(item=>item.Key.Length).ToArray();
    private static Template? CreateTemplate(string key)
    {
        var source=Normalize(key).Replace("${","{");
        var tokens=Regex.Matches(source,@"\{([^{}]+)\}");
        if(tokens.Count==0||Regex.Replace(source,@"\{[^{}]+\}","").Length<8)return null;
        var pattern=new StringBuilder("^");var offset=0;
        foreach(Match token in tokens){pattern.Append(Regex.Escape(source[offset..token.Index]));pattern.Append("(.+?)");offset=token.Index+token.Length;}
        pattern.Append(Regex.Escape(source[offset..]));pattern.Append('$');
        return new(key,new Regex(pattern.ToString(),RegexOptions.CultureInvariant|RegexOptions.Singleline,TimeSpan.FromMilliseconds(30)),tokens.Select(token=>token.Groups[1].Value).ToArray());
    }
    private string? Known(string raw,int depth=0)
    {
        if(Resources[Code].TryGetValue(raw,out var direct))return direct;
        var normalized=Normalize(raw);
        if(Keys.TryGetValue(normalized,out var key)&&Resources[Code].TryGetValue(key,out direct))return direct;
        if(depth>3||normalized.Length>8192)return null;
        foreach(var template in Templates)
        {
            Match match;try{match=template.Pattern.Match(normalized);}catch(RegexMatchTimeoutException){continue;}
            if(!match.Success||!Resources[Code].TryGetValue(template.Key,out var translated))continue;
            translated=translated.Replace("${","{");
            for(var i=0;i<template.Names.Length;i++)
            {
                var name=template.Names[i];var value=match.Groups[i+1].Value;
                if(Regex.IsMatch(name,"message|reason|error",RegexOptions.IgnoreCase))value=Known(value,depth+1)??this["Technical details are available in the diagnostic log."];
                translated=translated.Replace("{"+name+"}",value,StringComparison.Ordinal);
            }
            return translated;
        }
        return null;
    }
    // Translate only presentation. Raw exceptions and log payloads remain English.
    public string Error(string? raw)=>Present(raw,true);
    public string Message(string? raw)=>Present(raw,false);
    private string Present(string? raw,bool error)
    {
        if(string.IsNullOrWhiteSpace(raw))return "";
        if(Code=="en")return raw;
        var first=raw.Split('\n')[0].Trim();
        var text=Regex.Replace(first,@"^(?:Error|InvalidDataException|InvalidOperationException):\s*","");
        if(Known(text) is { } translated)return translated;
        var reference=Regex.Match(text,@"\bReference:\s*([a-zA-Z0-9-]{6,})\.?",RegexOptions.CultureInvariant);
        if(reference.Success)text=text.Remove(reference.Index,reference.Length).Trim();
        var parts=Regex.Split(text,@"(?<=[.!?])\s+");
        var result=parts.Length>1?string.Join(" ",parts.Select(part=>Known(part)??this["Technical details are available in the diagnostic log."])):Known(text);
        if(string.IsNullOrEmpty(result))result=this[error?"Something went wrong. Please try again.":"Status updated. See the diagnostic log for details."];
        if(reference.Success)result+=" "+this["Reference"]+": "+reference.Groups[1].Value+".";
        return result;
    }
}
