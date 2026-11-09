using System.Reflection;
using System.Text.Json;

namespace StickerApp.Shared.Services;

public sealed partial class AppLanguage
{
    public static readonly IReadOnlyList<(string Code,string Name)> Supported=[("en","English"),("nl","Nederlands"),("de","Deutsch"),("ro","Română"),("nb","Norsk bokmål")];
    private static readonly Dictionary<string,Dictionary<string,string>> Resources=Load();
    public string Code { get; private set; }="en";
    public event Action? Changed;
    public string this[string text]=>Known(text)??Resources["en"].GetValueOrDefault(text)??text;
    public void Set(string code){if(!Supported.Any(item=>item.Code==code))code="en";if(Code==code)return;Code=code;Changed?.Invoke();}
    private static Dictionary<string,Dictionary<string,string>> Load()
    {
        var assembly=typeof(AppLanguage).Assembly;
        return Supported.ToDictionary(item=>item.Code,item=>{
            using var source=assembly.GetManifestResourceStream($"StickerApp.Shared.Localization.{item.Code}.json")??throw new InvalidOperationException("Missing translation file: "+item.Code);
            return JsonSerializer.Deserialize<Dictionary<string,string>>(source)??new();
        });
    }
}
