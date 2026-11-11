namespace StickerApp.Shared.Pages.Packs;

public partial class Packs
{
    private Dictionary<string, string> BrowsePreferenceValues => new()
    {
        ["sort"] = sort, ["type"] = typeFilter, ["count"] = countFilter, ["storage"] = storageFilter,
        ["ownership"] = ownershipFilter, ["visibility"] = visibilityFilter, ["spoiler"] = spoilerFilter,
        ["content"] = contentFilter
    };
    private void RestoreBrowsePreferences(Dictionary<string, string> values)
    {
        sort = values["sort"]; typeFilter = values["type"]; countFilter = values["count"];
        storageFilter = values["storage"]; ownershipFilter = values["ownership"];
        visibilityFilter = values["visibility"]; spoilerFilter = values["spoiler"]; contentFilter = values["content"];
    }
}
