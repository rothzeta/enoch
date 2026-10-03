using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using Enoch.Protocol;

namespace Enoch.Storage.FileSystem;

public sealed partial class FileSystemRunStore
{
    const string PendingPublication = ".publication-pending";
    static readonly string[] AbandonedPublicationPrefixes = [".publication-prepare-", ".publication-completed-"];
    readonly string?[] publicationCleanupDirectories = new string?[PublicationStripeCount];
    sealed record JournalFile(string Destination, string Payload, string Sha256);
    sealed record JournalIntent(IReadOnlyList<JournalFile> Files);
    sealed record PreparedPublication<T>(RunManifest Manifest, IReadOnlyList<JournalFile> Files, T Response);
    sealed record PlanIdentity(string EventId, PublicationResponse Response);

    static void ValidateEventIdentity(string eventId)
    {
        if (string.IsNullOrWhiteSpace(eventId) || eventId.Length > 128)
        {
            throw new EnochException("invalid_event_id", "Event id is required and must be <=128 characters.");
        }
    }

    static long PlanSequence(string path)
    {
        if (!long.TryParse(Path.GetFileNameWithoutExtension(path), NumberStyles.None, CultureInfo.InvariantCulture, out var sequence) || sequence <= 0)
        {
            throw new EnochException("storage_corrupt", "Plan revision has an invalid sequence.", 500);
        }
        return sequence;
    }

    static bool IsCanonicalFile(string path)
    {
        if (path is "manifest.json" or "request.json" or "events.jsonl" or "result.json" or "checksums.sha256")
        {
            return true;
        }
        var parts = path.Split('/');
        if (parts.Length == 2 && parts[0] == "events")
        {
            return parts[1].EndsWith(".json", StringComparison.Ordinal)
                && long.TryParse(parts[1][..^5], NumberStyles.None, CultureInfo.InvariantCulture, out var eventSequence) && eventSequence > 0;
        }
        if (parts.Length == 2 && parts[0] == "plans")
        {
            var suffix = parts[1].EndsWith(".event-id", StringComparison.Ordinal) ? ".event-id" : ".json";
            return parts[1].EndsWith(suffix, StringComparison.Ordinal)
                && long.TryParse(parts[1][..^suffix.Length], NumberStyles.None, CultureInfo.InvariantCulture, out var sequence) && sequence > 0;
        }
        return parts.Length == 3 && (parts[0] is "evidence" or "artifacts")
            && Guid.TryParseExact(parts[1], "N", out var identity) && identity.ToString("N") == parts[1]
            && (parts[2] == "metadata.json" || parts[0] == "evidence" && parts[2] == "content" || parts[0] == "artifacts" && parts[2] == "file");
    }

    static IEnumerable<(string Destination, string Path)> CanonicalFiles(string directory) =>
        Directory.GetFiles(directory, "*", SearchOption.AllDirectories)
            .Select(path => (Destination: Path.GetRelativePath(directory, path).Replace('\\', '/'), Path: path))
            .Where(file => IsCanonicalFile(file.Destination) && file.Destination != "checksums.sha256");

    Task<T> CommitPublication<T>(string directory, RunManifest manifest, IReadOnlyDictionary<string, byte[]> replacements, T response, CancellationToken cancellationToken) =>
        CommitPreparedPublication(directory, async (stage, token) =>
        {
            var files = new List<JournalFile>();
            foreach (var replacement in replacements)
            {
                files.Add(await StagePublicationBytes(stage, replacement.Key, replacement.Value, files.Count, token));
            }
            return new PreparedPublication<T>(manifest, files, response);
        }, cancellationToken);

    static async Task<JournalFile> StagePublicationBytes(string stage, string destination, byte[] content, int index, CancellationToken cancellationToken)
    {
        var payload = $"{index:0000}.payload";
        await File.WriteAllBytesAsync(Path.Combine(stage, payload), content, cancellationToken);
        return new(destination, payload, Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant());
    }

    async Task<T> CommitPreparedPublication<T>(string directory, Func<string, CancellationToken, Task<PreparedPublication<T>>> prepare, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Directory.Exists(Path.Combine(directory, "checksums.sha256")))
        {
            throw new IOException("The checksum destination is a directory.");
        }
        var currentManifest = await ReadJson<RunManifest>(Path.Combine(directory, "manifest.json"), cancellationToken);
        var metadata = await GetPublicationMetadataAsync(directory, currentManifest, cancellationToken);
        var stage = Path.Combine(directory, $".publication-prepare-{Guid.NewGuid():N}");
        Directory.CreateDirectory(stage);
        var decisionMade = false;
        try
        {
            var prepared = await prepare(stage, cancellationToken);
            var manifest = prepared.Manifest;
            var files = prepared.Files.ToList();
            files.Add(await StagePublicationBytes(stage, "manifest.json", JsonSerializer.SerializeToUtf8Bytes(manifest, json), files.Count, cancellationToken));
            var checksums = new SortedDictionary<string, string>(metadata.Checksums, StringComparer.Ordinal);
            foreach (var file in files)
            {
                if (!IsCanonicalFile(file.Destination) || file.Destination is "request.json" or "checksums.sha256")
                {
                    throw new InvalidOperationException("Publication replacement is not a supported mutable bundle file.");
                }
                checksums[file.Destination] = file.Sha256;
            }
            var checksumContent = Encoding.UTF8.GetBytes(string.Join("\n", checksums.Select(entry => $"{entry.Value}  {entry.Key}")) + "\n");
            files.Add(await StagePublicationBytes(stage, "checksums.sha256", checksumContent, files.Count, cancellationToken));
            var descriptor = JsonSerializer.SerializeToUtf8Bytes(new JournalIntent(files), json);
            await File.WriteAllBytesAsync(Path.Combine(stage, "intent.json"), descriptor, cancellationToken);
            await File.WriteAllTextAsync(Path.Combine(stage, "intent.sha256"), Convert.ToHexString(SHA256.HashData(descriptor)).ToLowerInvariant(), cancellationToken);
            publicationCheckpoint?.Invoke(new(manifest.Id, ".publication-prepared", false));
            cancellationToken.ThrowIfCancellationRequested();
            Directory.Move(stage, Path.Combine(directory, PendingPublication));
            decisionMade = true;
            publicationCheckpoint?.Invoke(new(manifest.Id, PendingPublication, true));
            await RecoverPublication(directory, true);
            await AcceptPublicationMetadataAsync(directory, manifest, checksums, files);
            return prepared.Response;
        }
        catch (Exception error)
        {
            publicationCleanupDirectories[PublicationStripeIndex(Path.GetFileName(directory))] = null;
            if (decisionMade)
            {
                InvalidatePublicationMetadata(directory);
                if (error is not EnochException)
                {
                    throw new EnochException("publication_pending", "Publication was committed; retry after recovery completes.", 503);
                }
            }
            throw;
        }
        finally
        {
            if (Directory.Exists(stage))
            {
                Directory.Delete(stage, true);
            }
        }
    }

    async Task RecoverPublication(string directory, bool ownedCommit = false)
    {
        if (!Directory.Exists(directory))
        {
            return;
        }
        var pending = Path.Combine(directory, PendingPublication);
        var stripe = PublicationStripeIndex(Path.GetFileName(directory));
        if (File.Exists(pending))
        {
            InvalidatePublicationMetadata(directory);
            publicationCleanupDirectories[stripe] = null;
            throw new EnochException("storage_corrupt", "Publication journal is not a directory.", 500);
        }
        if (Directory.Exists(pending))
        {
            if (!ownedCommit)
            {
                InvalidatePublicationMetadata(directory);
                publicationCleanupDirectories[stripe] = null;
            }
            var intent = await ValidatePublicationIntent(pending);
            try
            {
                foreach (var file in intent.Files.OrderBy(file => file.Destination == "manifest.json" ? 1 : 0))
                {
                    var target = Path.Combine(directory, file.Destination);
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    var temporary = target + $".{Guid.NewGuid():N}.tmp";
                    try
                    {
                        File.Copy(Path.Combine(pending, file.Payload), temporary);
                        File.Move(temporary, target, true);
                    }
                    finally
                    {
                        if (File.Exists(temporary))
                        {
                            File.Delete(temporary);
                        }
                    }
                    publicationCheckpoint?.Invoke(new(Path.GetFileName(directory), file.Destination, true));
                }
                var completed = Path.Combine(directory, $".publication-completed-{Guid.NewGuid():N}");
                Directory.Move(pending, completed);
                publicationCheckpoint?.Invoke(new(Path.GetFileName(directory), ".publication-cleanup", true));
                Directory.Delete(completed, true);
            }
            catch (Exception error) when (error is not EnochException)
            {
                throw new EnochException("publication_pending", "Committed publication could not be recovered; repair the storage fault and retry.", 503);
            }
        }
        if (publicationCleanupDirectories[stripe] == directory)
        {
            return;
        }
        foreach (var abandoned in Directory.GetDirectories(directory).Where(IsAbandonedPublicationStage))
        {
            Directory.Delete(abandoned, true);
        }
        foreach (var temporary in Directory.GetFiles(directory, "*.tmp", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(directory, temporary).Replace('\\', '/');
            if (IsCanonicalTemporary(relative))
            {
                File.Delete(temporary);
            }
        }
        publicationCleanupDirectories[stripe] = directory;
    }

    static bool IsCanonicalTemporary(string path)
    {
        if (!path.EndsWith(".tmp", StringComparison.Ordinal))
        {
            return false;
        }
        var withoutExtension = path[..^4];
        var separator = withoutExtension.LastIndexOf('.');
        return separator > 0 && Guid.TryParseExact(withoutExtension[(separator + 1)..], "N", out _)
            && IsCanonicalFile(withoutExtension[..separator]);
    }

    static bool IsAbandonedPublicationStage(string directory)
    {
        var name = Path.GetFileName(directory);
        foreach (var prefix in AbandonedPublicationPrefixes)
        {
            if (name.StartsWith(prefix, StringComparison.Ordinal) && Guid.TryParseExact(name[prefix.Length..], "N", out _))
            {
                return true;
            }
        }
        return false;
    }

    async Task<JournalIntent> ValidatePublicationIntent(string pending)
    {
        try
        {
            var descriptor = await File.ReadAllBytesAsync(Path.Combine(pending, "intent.json"));
            var descriptorChecksum = await File.ReadAllTextAsync(Path.Combine(pending, "intent.sha256"));
            if (!string.Equals(Convert.ToHexString(SHA256.HashData(descriptor)), descriptorChecksum, StringComparison.OrdinalIgnoreCase))
            {
                throw new JsonException("Publication descriptor checksum does not match.");
            }
            var intent = JsonSerializer.Deserialize<JournalIntent>(descriptor, json);
            if (intent?.Files is null || intent.Files.Count < 2
                || !intent.Files.Any(file => file.Destination == "manifest.json")
                || !intent.Files.Any(file => file.Destination == "checksums.sha256")
                || intent.Files.Select(file => file.Destination).Distinct(StringComparer.Ordinal).Count() != intent.Files.Count)
            {
                throw new JsonException("Publication journal is incomplete.");
            }
            foreach (var file in intent.Files)
            {
                if (!IsCanonicalFile(file.Destination) || file.Destination == "request.json"
                    || !file.Payload.EndsWith(".payload", StringComparison.Ordinal)
                    || !int.TryParse(file.Payload[..^8], NumberStyles.None, CultureInfo.InvariantCulture, out _))
                {
                    throw new JsonException("Publication journal references an invalid destination or payload.");
                }
                await using var payload = File.OpenRead(Path.Combine(pending, file.Payload));
                if (!string.Equals(Convert.ToHexString(await SHA256.HashDataAsync(payload)), file.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    throw new JsonException("Publication journal payload checksum does not match.");
                }
            }
            return intent;
        }
        catch (Exception error) when (error is IOException or JsonException or NullReferenceException)
        {
            throw new EnochException("storage_corrupt", "Publication journal is incomplete or corrupt; preserve it for operator repair.", 500);
        }
    }
}
