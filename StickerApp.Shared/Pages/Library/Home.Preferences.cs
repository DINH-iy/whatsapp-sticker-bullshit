namespace StickerApp.Shared.Pages.Library;

public partial class Home
{
    private string BrowsePreferenceScope => PackId is null ? "library" : "pack-stickers";
    private Dictionary<string, string> BrowsePreferenceValues => new()
    {
        ["sort"] = sort, ["type"] = typeFilter, ["format"] = formatFilter, ["storage"] = storageFilter,
        ["editing"] = editingFilter, ["spoiler"] = spoilerFilter, ["favorites"] = favoritesFilter,
        ["membership"] = membershipFilter, ["folder"] = folderFilter
    };
    private void RestoreBrowsePreferences(Dictionary<string, string> values)
    {
        sort = values["sort"]; typeFilter = values["type"]; formatFilter = values["format"];
        storageFilter = values["storage"]; editingFilter = values["editing"]; spoilerFilter = values["spoiler"];
        favoritesFilter = values["favorites"]; membershipFilter = values["membership"];
        var folder = values.GetValueOrDefault("folder", "all");
        folderFilter = folder is "all" or "none" || library.Folders.Any(item => item.Id.ToString() == folder) ? folder : "all";
    }
}
