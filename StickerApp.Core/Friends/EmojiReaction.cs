using System.Globalization;
using System.Text;

namespace StickerApp.Core;

public static class EmojiReaction
{
    public static bool IsValid(string? value)
    {
        if(string.IsNullOrEmpty(value)||value.Length>64||StringInfo.ParseCombiningCharacters(value).Length!=1)return false;
        var runes=value.EnumerateRunes().Select(rune=>rune.Value).ToArray();
        var keycap=runes.Contains(0x20e3);
        static bool Symbol(int rune)=>rune is >=0x1f000 and <=0x1faff or >=0x2600 and <=0x27bf or >=0x2194 and <=0x2199 or >=0x23cf and <=0x23fa or >=0x25fb and <=0x25fe or >=0x2b05 and <=0x2b07 or 0xa9 or 0xae or 0x203c or 0x2049 or 0x2122 or 0x2139 or 0x21a9 or 0x21aa or 0x231a or 0x231b or 0x2328 or 0x24c2 or 0x25aa or 0x25ab or 0x25b6 or 0x25c0 or 0x2934 or 0x2935 or 0x2b1b or 0x2b1c or 0x2b50 or 0x2b55 or 0x3030 or 0x303d or 0x3297 or 0x3299;
        return (keycap||runes.Any(Symbol))&&runes.All(rune=>Symbol(rune)||rune is 0xfe0e or 0xfe0f or 0x200d or >=0xe0020 and <=0xe007f||keycap&&(rune is >=48 and <=57 or 35 or 42 or 0x20e3));
    }
}
