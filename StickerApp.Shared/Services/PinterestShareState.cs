namespace StickerApp.Shared.Services;

public sealed record PendingPinterestShare(string Id, string Url);

public interface IPinterestShareInbox
{
    PendingPinterestShare? Peek();
    void Remove(string id);
}

public sealed class EmptyPinterestShareInbox : IPinterestShareInbox
{
    public PendingPinterestShare? Peek() => null;
    public void Remove(string id) { }
}

public sealed class PinterestShareState
{
    public event Action? Changed;
    public void CheckInbox() => Changed?.Invoke();
}
