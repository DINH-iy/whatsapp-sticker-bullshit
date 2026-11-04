namespace StickerApp.ShareServer;

public sealed class PinterestOptions
{
    public string ClientId { get; set; } = "1634470";
    public string ClientSecret { get; set; } = "";
    public string RedirectUri { get; set; } = "https://localhost:5001/api/pinterest/callback";
    public string PostLoginRedirect { get; set; } = "stickerstudio://pinterest-auth";
}
