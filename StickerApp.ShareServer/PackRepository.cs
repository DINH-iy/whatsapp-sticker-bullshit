using System.Security.Cryptography;
using System.Text.Json;
using StickerApp.Core;

namespace StickerApp.ShareServer;

/// <summary>Stores immutable, content-addressed archives, independently of the app's library.</summary>
public sealed class PackRepository
{
    private readonly string root;
    private readonly long maximumStorageBytes;
    private readonly SemaphoreSlim commitGate = new(1, 1);

    public sealed record PackDetails(string Name, int ImageCount, bool Animated, long Bytes, string Sha256)
    {
        public string FileName => PackArchive.FileNameFor(Name);
    }

    public PackRepository(IConfiguration configuration)
    {
        root = Path.GetFullPath(configuration["Sharing:StoragePath"] ?? ".data/shared-packs");
        maximumStorageBytes = configuration.GetValue<long?>("Sharing:MaxStorageBytes") ?? 2L * 1024 * 1024 * 1024;
        if (maximumStorageBytes < PackArchive.MaxArchiveBytes)
            throw new InvalidOperationException("Sharing:MaxStorageBytes must allow at least one 100 MB pack.");
        Directory.CreateDirectory(root);
    }

    public static bool IsValidHash(string hash) => hash.Length == 64 && hash.All(character =>
        character is >= '0' and <= '9' or >= 'a' and <= 'f');

    public string ArchivePath(string hash) => Path.Combine(root, hash + ".zip");
    private string MetadataPath(string hash) => Path.Combine(root, hash + ".json");

    public async Task<PackDetails?> FindAsync(string hash)
    {
        if (!IsValidHash(hash) || !File.Exists(ArchivePath(hash)) || !File.Exists(MetadataPath(hash)))
            return null;
        await using var input = File.OpenRead(MetadataPath(hash));
        return await JsonSerializer.DeserializeAsync<PackDetails>(input);
    }

    public async Task<PackDetails> PublishAsync(Stream request, CancellationToken cancellationToken)
    {
        var temporaryPath = Path.Combine(root, $"upload-{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var output = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write))
            {
                var buffer = new byte[64 * 1024];
                long total = 0;
                int count;
                while ((count = await request.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    total += count;
                    if (total > PackArchive.MaxArchiveBytes)
                        throw new InvalidDataException("Pack files may not exceed 100 MB.");
                    await output.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
                }
            }

            PackDetails details;
            await using (var input = File.OpenRead(temporaryPath))
            {
                var imported = await PackArchive.ReadAsync(input);
                input.Position = 0;
                var hash = Convert.ToHexString(await SHA256.HashDataAsync(input, cancellationToken)).ToLowerInvariant();
                details = new PackDetails(imported.Name, imported.Images.Count, imported.Animated, input.Length, hash);
            }

            await commitGate.WaitAsync(cancellationToken);
            try
            {
                if (File.Exists(ArchivePath(details.Sha256)))
                {
                    // Repair metadata if an earlier process stopped during its final commit.
                    if (!File.Exists(MetadataPath(details.Sha256)))
                        await File.WriteAllTextAsync(MetadataPath(details.Sha256), JsonSerializer.Serialize(details), cancellationToken);
                    return details;
                }
                var usedBytes = Directory.EnumerateFiles(root, "*.zip").Sum(file => new FileInfo(file).Length);
                if (usedBytes + details.Bytes > maximumStorageBytes) throw new SharingStorageFullException();

                var metadata = MetadataPath(details.Sha256);
                await File.WriteAllTextAsync(metadata + ".tmp", JsonSerializer.Serialize(details), cancellationToken);
                File.Move(metadata + ".tmp", metadata, overwrite: true);
                File.Move(temporaryPath, ArchivePath(details.Sha256));
                return details;
            }
            finally
            {
                commitGate.Release();
            }
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    public async Task DeleteAsync(string hash)
    {
        await commitGate.WaitAsync();
        try
        {
            File.Delete(ArchivePath(hash));
            File.Delete(MetadataPath(hash));
        }
        finally
        {
            commitGate.Release();
        }
    }
}

public sealed class SharingStorageFullException : IOException;
