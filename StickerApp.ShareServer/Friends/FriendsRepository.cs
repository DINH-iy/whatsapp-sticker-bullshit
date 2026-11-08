using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using StickerApp.Core;

namespace StickerApp.ShareServer.Friends;

// Friends-only archives are deliberately separate from the anonymous /p/{hash} store.
public sealed partial class FriendsRepository
{
    private readonly string root;
    private readonly byte[]? inviteHash;
    private readonly long limit, memberDailyBytes, groupDailyBytes;
    private readonly SemaphoreSlim gate = new(1, 1);
    private string IndexPath => Path.Combine(root, "friends.json");
    public bool Enabled => inviteHash is not null;
    public sealed record Member(Guid Id, string Username, string TokenHash);
    public sealed record StoredPack(Guid OwnerId, Guid PackId, string Name, string Publisher, int Count, bool Animated,
        bool Nsfw, string? Cover, string File, long Bytes, DateTimeOffset UpdatedAt);
    public sealed record PackCard(Guid OwnerId, Guid PackId, string Username, string Name, string Publisher, int Count,
        bool Animated, bool Nsfw, string? Cover, long Bytes, DateTimeOffset UpdatedAt);
    public sealed record Transfer(Guid MemberId, DateOnly Day, long Bytes);
    public sealed record Removal(Guid OwnerId, Guid PackId, DateTimeOffset At);
    public sealed partial class State { public List<Member> Members { get; set; } = []; public List<StoredPack> Packs { get; set; } = []; public List<Removal> Removals { get; set; } = []; public List<Transfer> Transfers { get; set; } = []; }
    public sealed record Joined(Guid Id, string Username, string Token);

    public FriendsRepository(IConfiguration config)
    {
        root = Path.GetFullPath(config["Friends:StoragePath"] ?? ".data/friends");
        limit = config.GetValue<long?>("Friends:MaxStorageBytes") ?? 2L * 1024 * 1024 * 1024;
        if (limit < PackArchive.MaxArchiveBytes) throw new InvalidOperationException("Friends storage must allow at least 100 MB.");
        memberDailyBytes = config.GetValue<long?>("Friends:MemberDailyTransferBytes") ?? 128L * 1024 * 1024;
        groupDailyBytes = config.GetValue<long?>("Friends:GroupDailyTransferBytes") ?? 512L * 1024 * 1024;
        if (memberDailyBytes < 1 || groupDailyBytes < 1) throw new InvalidOperationException("Friends transfer limits must be positive.");
        GroupName = config["Friends:GroupName"] ?? "Friends’ group";
        if (GroupName.Length is <1 or >80 || GroupName.Any(char.IsControl)) throw new InvalidOperationException("Choose a group name of 1–80 characters.");
        var invite = config["Friends:InviteCode"];
        if (!string.IsNullOrEmpty(invite))
        {
            if (invite.Length is < 24 or > 512) throw new InvalidOperationException("Friends__InviteCode must be a random invitation code of 24–512 characters.");
            inviteHash = Hash(invite);
        }
        Directory.CreateDirectory(root);
    }
    private static byte[] Hash(string value) => SHA256.HashData(Encoding.UTF8.GetBytes(value));
    private async Task<State> Read() => File.Exists(IndexPath)
        ? JsonSerializer.Deserialize<State>(await File.ReadAllTextAsync(IndexPath)) ?? throw new InvalidDataException("Friends index is empty.") : new();
    private async Task Save(State state)
    {
        var temporary = IndexPath + ".tmp";
        await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(state));
        File.Move(temporary, IndexPath, true);
    }
    public async Task<Joined?> Join(string code, string username)
    {
        if (!Enabled || code.Length > 512) return null;
        var bootstrap = CryptographicOperations.FixedTimeEquals(Hash(code), inviteHash!);
        username = username.Trim();
        if (username.Length is < 1 or > 40 || username.Any(char.IsControl)) throw new InvalidDataException("Choose a username of 1–40 characters.");
        await gate.WaitAsync();
        try
        {
            var state = await Read();
            var hash = Hash(code.Trim().ToLowerInvariant());
            var invitation = state.Invitations.FirstOrDefault(invite => invite.ExpiresAt > DateTimeOffset.UtcNow && invite.RemainingUses > 0 && CryptographicOperations.FixedTimeEquals(Convert.FromHexString(invite.Hash), hash));
            if (!bootstrap && invitation is null) return null;
            if (state.Members.Count >= 200) throw new InvalidDataException("This friends group is full.");
            var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
            var member = new Member(Guid.NewGuid(), username, Convert.ToHexString(Hash(token)));
            if (!bootstrap && invitation is not null)
            {
                state.Invitations.Remove(invitation);
                if (invitation.RemainingUses > 1) state.Invitations.Add(invitation with { RemainingUses = invitation.RemainingUses - 1 });
            }
            state.Members.Add(member); await Save(state);
            return new(member.Id, member.Username, token);
        }
        finally { gate.Release(); }
    }
    public async Task<Member?> Authenticate(string? token)
    {
        if (!Enabled || token is null || token.Length != 64) return null;
        var hash = Hash(token);
        await gate.WaitAsync();
        try { return (await Read()).Members.FirstOrDefault(member => CryptographicOperations.FixedTimeEquals(Convert.FromHexString(member.TokenHash), hash)); }
        finally { gate.Release(); }
    }
    public async Task<(List<PackCard> Items, int Total)> List(int offset)
    {
        await gate.WaitAsync();
        try
        {
            var state = await Read();
            var all = state.Packs.OrderByDescending(pack => pack.UpdatedAt).ThenBy(pack => pack.PackId).ToArray();
            return (all.Skip(Math.Max(0, offset)).Take(12).Select(pack => new PackCard(pack.OwnerId, pack.PackId,
                state.Members.Single(member => member.Id == pack.OwnerId).Username, pack.Name, pack.Publisher, pack.Count,
                pack.Animated, pack.Nsfw, pack.Cover, pack.Bytes, pack.UpdatedAt)).ToList(), all.Length);
        }
        finally { gate.Release(); }
    }
    public async Task Publish(Member owner, Guid id, Stream input, CancellationToken cancellation)
    {
        var started = DateTimeOffset.UtcNow;
        var temporary = Path.Combine(root, Guid.NewGuid().ToString("N") + ".upload");
        string? committed = null;
        try
        {
            await using (var output = File.Create(temporary))
            {
                var buffer = new byte[65536]; long total = 0; int count;
                while ((count = await input.ReadAsync(buffer, cancellation)) > 0)
                {
                    total += count;
                    if (total > PackArchive.MaxArchiveBytes) throw new InvalidDataException("A pack may not exceed 100 MB.");
                    await output.WriteAsync(buffer.AsMemory(0, count), cancellation);
                }
            }
            PackArchive.ImportedPack pack;
            await using (var file = File.OpenRead(temporary)) pack = await PackArchive.ReadAsync(file);
            if (!pack.Public || pack.IsWhatsAppFormat) throw new InvalidDataException("Only a public editable ZIP backup can be shared with friends.");
            if (pack.Images.Count == 0) throw new InvalidDataException("Add at least one sticker before sharing.");
            await gate.WaitAsync(cancellation);
            try
            {
                var state = await Read();
                if (!state.Members.Any(member => member.Id == owner.Id && member.TokenHash == owner.TokenHash)) throw new UnauthorizedAccessException();
                if (state.Removals.Any(removal => removal.OwnerId == owner.Id && removal.PackId == id && removal.At >= started))
                    throw new InvalidDataException("This pack became private while syncing. Its upload was cancelled.");
                var previous = state.Packs.FirstOrDefault(p => p.OwnerId == owner.Id && p.PackId == id);
                if (previous is null && state.Packs.Count(p => p.OwnerId == owner.Id) >= 10) throw new InvalidDataException("You can share up to 10 packs.");
                var bytes = new FileInfo(temporary).Length;
                if (state.Packs.Sum(p => p.Bytes) + state.Collections.Sum(collection => collection.Bytes) - (previous?.Bytes ?? 0) + bytes > limit) throw new SharingStorageFullException();
                var name = Guid.NewGuid().ToString("N") + ".zip";
                committed = Path.Combine(root, name); File.Move(temporary, committed);
                state.Packs.RemoveAll(p => p.OwnerId == owner.Id && p.PackId == id);
                state.Packs.Add(new(owner.Id, id, pack.Name, pack.Publisher, pack.Images.Count, pack.Animated,
                    pack.Nsfw || pack.Images[0].Nsfw, pack.CoverImage, name, bytes, DateTimeOffset.UtcNow));
                await Save(state); committed = null;
                if (previous is not null) TryDelete(Path.Combine(root, previous.File));
            }
            finally { gate.Release(); }
        }
        finally { TryDelete(temporary); if (committed is not null) TryDelete(committed); }
    }
    public async Task<bool> ReserveTransfer(Member member, long bytes)
    {
        if (bytes < 0 || bytes > PackArchive.MaxArchiveBytes) return false;
        await gate.WaitAsync();
        try
        {
            var state = await Read(); var day = DateOnly.FromDateTime(DateTime.UtcNow);
            if (!state.Members.Any(value => value.Id == member.Id && value.TokenHash == member.TokenHash)) return false;
            state.Transfers.RemoveAll(transfer => transfer.Day != day);
            var old = state.Transfers.FirstOrDefault(transfer => transfer.MemberId == member.Id);
            if ((old?.Bytes ?? 0) + bytes > memberDailyBytes || state.Transfers.Sum(transfer => transfer.Bytes) + bytes > groupDailyBytes) return false;
            if (old is not null) state.Transfers.Remove(old);
            state.Transfers.Add(new(member.Id, day, (old?.Bytes ?? 0) + bytes)); await Save(state); return true;
        }
        finally { gate.Release(); }
    }
    public async Task Leave(Member member)
    {
        await gate.WaitAsync();
        try
        {
            var state = await Read();
            var files = state.Packs.Where(pack => pack.OwnerId == member.Id).Select(pack => pack.File).ToArray();
            state.Packs.RemoveAll(pack => pack.OwnerId == member.Id); state.Members.RemoveAll(value => value.Id == member.Id);
            state.Invitations.RemoveAll(invite => invite.CreatedBy == member.Id);
            state.Removals.RemoveAll(removal => removal.OwnerId == member.Id);
            foreach (var collection in state.Collections)
                foreach (var sticker in collection.Stickers) sticker.Reactions.Remove(member.Id);
            await Save(state);
            foreach (var file in files) TryDelete(Path.Combine(root, file));
        }
        finally { gate.Release(); }
    }
    public async Task<bool> Remove(Member owner, Guid id)
    {
        await gate.WaitAsync();
        try
        {
            var state = await Read(); var old = state.Packs.FirstOrDefault(p => p.OwnerId == owner.Id && p.PackId == id);
            if (old is null) return false;
            state.Packs.Remove(old);
            state.Removals.RemoveAll(removal => removal.OwnerId == owner.Id && removal.PackId == id);
            state.Removals.Add(new(owner.Id, id, DateTimeOffset.UtcNow));
            var expired = state.Removals.Where(removal => removal.OwnerId == owner.Id).OrderByDescending(removal => removal.At).Skip(100).ToList();
            state.Removals.RemoveAll(expired.Contains);
            await Save(state); TryDelete(Path.Combine(root, old.File)); return true;
        }
        finally { gate.Release(); }
    }
    // The index is checked on every request. A removed/private pack has no downloadable route.
    public async Task<(Stream Stream, string Name)?> Open(Guid owner, Guid id)
    {
        await gate.WaitAsync();
        try
        {
            var pack = (await Read()).Packs.FirstOrDefault(p => p.OwnerId == owner && p.PackId == id);
            if (pack is null) return null;
            return (new FileStream(Path.Combine(root, pack.File), FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete), PackArchive.FileNameFor(pack.Name));
        }
        finally { gate.Release(); }
    }
    private static void TryDelete(string path) { try { File.Delete(path); } catch (IOException) { } }
}
