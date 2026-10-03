using System.Buffers;
using System.Security.Cryptography;
using Enoch.Protocol;

namespace Enoch.Storage.FileSystem;

public sealed partial class FileSystemRunStore
{
    private const int PublicationStripeCount = 64;
    private readonly SemaphoreSlim[] publicationGates = CreatePublicationGates();

    private static SemaphoreSlim[] CreatePublicationGates()
    {
        var gates = new SemaphoreSlim[PublicationStripeCount];
        for (var index = 0; index < gates.Length; index++)
        {
            gates[index] = new SemaphoreSlim(1, 1);
        }
        return gates;
    }

    private int PublicationStripeIndex(string id)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ValidateId(id);
        uint hash = 2166136261;
        foreach (var character in id)
        {
            hash = unchecked((hash ^ character) * 16777619);
        }
        return (int)(hash % PublicationStripeCount);
    }

    private SemaphoreSlim PublicationGate(string id) => publicationGates[PublicationStripeIndex(id)];

    private static async Task<(long Length, string Sha256)> CopyUploadAsync(Stream input, Stream output, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var buffer = ArrayPool<byte>.Shared.Rent(64 * 1024);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        long length = 0;
        try
        {
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                var count = (int)Math.Min(buffer.Length, MaxUpload - length + 1);
                var read = await input.ReadAsync(buffer.AsMemory(0, count), ct);
                if (read == 0)
                {
                    return (length, Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant());
                }
                length += read;
                if (length > MaxUpload)
                {
                    throw new EnochException("too_large", "Artifact exceeds limit.", 413);
                }
                hash.AppendData(buffer, 0, read);
                await output.WriteAsync(buffer.AsMemory(0, read), ct);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }
}
