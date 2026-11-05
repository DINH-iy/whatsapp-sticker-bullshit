namespace StickerApp.Core;

/// <summary>Immutable files plus atomic rename allow the extension and app to share a durable queue.</summary>
public static class PinterestShareInboxFiles
{
    public const string DirectoryName = "PinterestInbox";

    public static void Save(string container, string text)
    {
        var url = PinterestShareLink.Parse(text);
        var folder = Path.Combine(container, DirectoryName);
        Directory.CreateDirectory(folder);
        if (Directory.EnumerateFiles(folder, "*.txt").Take(100).Count() >= 100)
            throw new IOException("Open Stickers and import or discard your saved links before saving more.");
        var name = $"{DateTime.UtcNow.Ticks:D19}-{Guid.NewGuid():N}";
        var temporary = Path.Combine(folder, name + ".tmp");
        try
        {
            File.WriteAllText(temporary, url.AbsoluteUri);
            File.Move(temporary, Path.Combine(folder, name + ".txt"));
        }
        finally { File.Delete(temporary); }
    }

    public static (string Id, string Url)? Peek(string container)
    {
        var folder = Path.Combine(container, DirectoryName);
        if (!Directory.Exists(folder)) return null;
        foreach (var path in Directory.EnumerateFiles(folder, "*.txt").Order(StringComparer.Ordinal))
        {
            if (new FileInfo(path).Length > PinterestShareLink.MaxTextLength) continue;
            var text = File.ReadAllText(path);
            if (PinterestShareLink.TryParse(text, out var url))
                return (Path.GetFileName(path), url.AbsoluteUri);
        }
        return null;
    }

    public static void Remove(string container, string id)
    {
        if (string.IsNullOrEmpty(id) || id != Path.GetFileName(id) || !id.EndsWith(".txt", StringComparison.Ordinal))
            throw new InvalidDataException("Invalid saved-link identifier.");
        File.Delete(Path.Combine(container, DirectoryName, id));
    }
}
