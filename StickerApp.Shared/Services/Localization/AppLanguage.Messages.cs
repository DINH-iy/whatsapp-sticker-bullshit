using System.Text;
using System.Text.RegularExpressions;

namespace StickerApp.Shared.Services;

public sealed partial class AppLanguage
{
    private static string Normalize(string text)=>Regex.Replace(text.Trim(),@"\s+"," ");
    private static readonly Lazy<Dictionary<string,string>> Keys=new(()=>Resources["en"].Keys.GroupBy(Normalize,StringComparer.Ordinal).ToDictionary(group=>group.Key,group=>group.First(),StringComparer.Ordinal));
    private static readonly Lazy<Dictionary<string,HashSet<string>>> Values=new(()=>Resources.ToDictionary(pair=>pair.Key,pair=>pair.Value.Values.Select(Normalize).ToHashSet(StringComparer.Ordinal)));
    private sealed record Template(string Key,Regex Pattern,string[] Names);
    private static readonly Lazy<Template[]> Templates=new(()=>Resources["en"].Keys.Where(key=>key.Contains('{')).Select(CreateTemplate).OfType<Template>().OrderByDescending(item=>item.Key.Length).ToArray());
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
        if(Values.Value[Code].Contains(normalized))return raw;
        if(Keys.Value.TryGetValue(normalized,out var key)&&Resources[Code].TryGetValue(key,out direct))return direct;
        if(depth>3||normalized.Length>8192)return null;
        foreach(var template in Templates.Value)
        {
            Match match;try{match=template.Pattern.Match(normalized);}catch(RegexMatchTimeoutException){continue;}
            if(!match.Success||!Resources[Code].TryGetValue(template.Key,out var translated))continue;
            translated=translated.Replace("${","{");
            for(var i=0;i<template.Names.Length;i++)
            {
                var name=template.Names[i];var value=match.Groups[i+1].Value;
                if(Regex.IsMatch(name,"message|reason|error|changes",RegexOptions.IgnoreCase))
                    value=Known(value,depth+1)??string.Join("; ",value.Split("; ").Select(part=>Known(part,depth+1)??this["Technical details are available in the diagnostic log."]));
                else if(Regex.IsMatch(name,@"^(kind|unit|type|label|title)$|\.Label$",RegexOptions.IgnoreCase))value=Known(value,depth+1)??value;
                translated=translated.Replace("{"+name+"}",value,StringComparison.Ordinal);
            }
            return translated;
        }
        return null;
    }
    public string EditorLabels()=>System.Text.Json.JsonSerializer.Serialize(new[]{"Move image","Rotate image","Rotate selected layer","Resize image {0}","Resize layer {0}","Rotate: {0}\u00b0. Drag or use arrow keys.","Resize: {0} \u00d7 {1}. Drag or use arrow keys.","top left","top","top right","left","right","bottom left","bottom","bottom right"}.ToDictionary(key=>key,key=>this[key]));
    public string Format(string template, params object?[] values)
    {
        var translated=this[template];
        return Regex.Replace(translated,@"\{(\d+)\}",match=>int.TryParse(match.Groups[1].Value,out var index)&&index<values.Length?Convert.ToString(values[index],System.Globalization.CultureInfo.CurrentCulture)??"":match.Value);
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
