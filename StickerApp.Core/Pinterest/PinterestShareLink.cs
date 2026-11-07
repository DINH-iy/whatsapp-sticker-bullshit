using System.Text.RegularExpressions;

namespace StickerApp.Core;

public static partial class PinterestShareLink
{
    public const string AppGroup = "group.com.stickerstudio.app";
    public const int MaxTextLength = 4096;

    public static bool TryParse(string? text, out Uri url)
    {
        url = null!;
        if (string.IsNullOrWhiteSpace(text) || text.Length > MaxTextLength)
            return false;
        foreach (Match match in Links().Matches(text))
        {
            var value = match.Value.TrimEnd('.', ',', '!', '?', ')', ']', '}');
            if (!Uri.TryCreate(value, UriKind.Absolute, out var candidate) ||
                candidate.Scheme != "https" || candidate.Port != 443 ||
                candidate.UserInfo.Length != 0 || candidate.Fragment.Length != 0)
                continue;
            var path = candidate.AbsolutePath;
            if ((candidate.Host == "pin.it" && ShortPath().IsMatch(path)) ||
                (candidate.Host is "pinterest.com" or "www.pinterest.com" && SelectionPath().IsMatch(path)))
            {
                url = candidate;
                return true;
            }
        }
        return false;
    }

    public static Uri Parse(string? text) => TryParse(text, out var url) ? url
        : throw new InvalidDataException("Paste a public Pinterest board or selection link (pin.it or pinterest.com).");

    [GeneratedRegex("https://[^\\s<>\"']+", RegexOptions.IgnoreCase)]
    private static partial Regex Links();
    [GeneratedRegex("^/[A-Za-z0-9_-]+/?$")]
    private static partial Regex ShortPath();
    [GeneratedRegex("^/(?:multi-pin-share/[A-Za-z0-9_-]+|(?!(?:pin|ideas|search|settings|login|signup|business|resource)/)[A-Za-z0-9_-]+/[A-Za-z0-9_-]+)/?$")]
    private static partial Regex SelectionPath();
}
