using System.Text.Json;

namespace StickerApp.Core;

public sealed record LocalProfile(string Username = "", string FirstName = "", string? Picture = null, bool SetupComplete = false);

public sealed partial class LibraryStore
{
    private string ProfileFile => Path.Combine(DefaultRoot, "profile.json");

    public async Task<LocalProfile> ReadProfileAsync()
    {
        await gate.WaitAsync();
        try
        {
            return File.Exists(ProfileFile)
                ? JsonSerializer.Deserialize<LocalProfile>(await File.ReadAllTextAsync(ProfileFile), Json) ?? new()
                : new();
        }
        finally { gate.Release(); }
    }

    public async Task SaveProfileAsync(LocalProfile profile)
    {
        var username = profile.Username.Trim();
        var firstName = profile.FirstName.Trim();
        if (username.Length > 40 || firstName.Length > 80 || username.Any(char.IsControl) || firstName.Any(char.IsControl))
            throw new InvalidDataException("Use a username up to 40 characters and a first name up to 80 characters.");
        if (profile.Picture is not null) PackCover.ReadBytes(profile.Picture);
        await gate.WaitAsync();
        try
        {
            await File.WriteAllTextAsync(ProfileFile + ".tmp", JsonSerializer.Serialize(profile with { Username = username, FirstName = firstName }, Json));
            File.Move(ProfileFile + ".tmp", ProfileFile, true);
        }
        finally { gate.Release(); }
        Changed?.Invoke();
    }
}
