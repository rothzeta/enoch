using System.Security.Cryptography;
using System.Text.Json;
using Enoch.Protocol;

namespace Enoch.Storage.FileSystem;

public sealed partial class FileSystemRunStore
{
    private sealed record EventLocator(string RelativePath, long LegacyLine = -1);
    private sealed record PublicationMetadata(
        string Directory,
        long Sequence,
        SortedDictionary<string, string> Checksums,
        Dictionary<string, string> PlanIdentities,
        Dictionary<string, EventLocator> EventIdentities);

    // A stripe retains metadata for its most recently used run. Payload objects
    // and reader lists never enter this cache; per-run indexes still grow with history.
    private readonly PublicationMetadata?[] publicationMetadata = new PublicationMetadata?[PublicationStripeCount];

    private void InvalidatePublicationMetadata(string directory)
    {
        var index = PublicationStripeIndex(Path.GetFileName(directory));
        if (publicationMetadata[index]?.Directory == directory)
        {
            publicationMetadata[index] = null;
        }
    }

    private async Task<PublicationMetadata> GetPublicationMetadataAsync(string directory, RunManifest manifest, CancellationToken ct)
    {
        if (manifest.Id != Path.GetFileName(directory))
        {
            throw CorruptHistory();
        }
        var index = PublicationStripeIndex(manifest.Id);
        var cached = publicationMetadata[index];
        if (cached?.Directory == directory && cached.Sequence == manifest.Sequence)
        {
            return cached;
        }
        // Eviction also drops a mismatching run's entire locator/inventory state.
        publicationMetadata[index] = null;
        try
        {
            var checksums = await VerifiedChecksumsAsync(directory, manifest.Id, ct);
            var history = await ValidateHistoryAsync(directory, manifest, ct);
            var metadata = new PublicationMetadata(directory, manifest.Sequence, checksums, history.PlanIdentities, history.EventIdentities);
            publicationMetadata[index] = metadata;
            return metadata;
        }
        catch (Exception error) when (error is IOException or JsonException or ArgumentException or NullReferenceException)
        {
            throw CorruptHistory();
        }
    }

    private sealed record PublicationHistory(Dictionary<string, string> PlanIdentities, Dictionary<string, EventLocator> EventIdentities);

    private async Task<PublicationHistory> ValidateHistoryAsync(string directory, RunManifest manifest, CancellationToken ct)
    {
        if (manifest.Id != Path.GetFileName(directory))
        {
            throw CorruptHistory();
        }
        var cached = publicationMetadata[PublicationStripeIndex(manifest.Id)];
        if (cached?.Directory == directory && cached.Sequence == manifest.Sequence)
        {
            return new(cached.PlanIdentities, cached.EventIdentities);
        }
        try
        {
            var planIdentities = new Dictionary<string, string>(StringComparer.Ordinal);
            var eventIdentities = new Dictionary<string, EventLocator>(StringComparer.Ordinal);
            var sequences = new HashSet<long>();
            var plansDirectory = Path.Combine(directory, "plans");
            var planFiles = Directory.Exists(plansDirectory) ? Directory.GetFiles(plansDirectory, "*.json") : [];
            foreach (var file in planFiles)
            {
                ct.ThrowIfCancellationRequested();
                var sequence = PlanSequence(file);
                if (!sequences.Add(sequence))
                {
                    throw CorruptHistory();
                }
                var payload = await ReadJson<object>(file, ct);
                if (payload is JsonElement { ValueKind: JsonValueKind.Null or JsonValueKind.Undefined })
                {
                    throw CorruptHistory();
                }
            }
            var identityFiles = Directory.Exists(plansDirectory) ? Directory.GetFiles(plansDirectory, "*.event-id") : [];
            foreach (var file in identityFiles)
            {
                var receipt = await ReadJson<PlanIdentity>(file, ct);
                var sequence = PlanSequence(file[..^".event-id".Length] + ".json");
                if (string.IsNullOrWhiteSpace(receipt.EventId) || receipt.EventId.Length > 128
                    || receipt.Response is null || receipt.Response.RunId != manifest.Id
                    || receipt.Response.Sequence != sequence || !Enum.IsDefined(receipt.Response.State)
                    || !File.Exists(file[..^".event-id".Length] + ".json")
                    || !planIdentities.TryAdd(receipt.EventId, Path.GetRelativePath(directory, file).Replace('\\', '/')))
                {
                    throw CorruptHistory();
                }
            }
            await foreach (var entry in StoredEventsAsync(directory, ct))
            {
                if (!sequences.Add(entry.Event.Sequence)
                    || !eventIdentities.TryAdd(entry.Event.EventId, entry.Locator))
                {
                    throw CorruptHistory();
                }
            }
            if (manifest.Sequence < 0 || sequences.Count != manifest.Sequence
                || sequences.Any(sequence => sequence <= 0 || sequence > manifest.Sequence))
            {
                throw CorruptHistory();
            }
            return new(planIdentities, eventIdentities);
        }
        catch (Exception error) when (error is IOException or JsonException or ArgumentException or NullReferenceException)
        {
            throw CorruptHistory();
        }
    }

    private async Task<SortedDictionary<string, string>> VerifiedChecksumsAsync(string directory, string id, CancellationToken ct)
    {
        var checksums = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in await File.ReadAllLinesAsync(Path.Combine(directory, "checksums.sha256"), ct))
        {
            if (line.Length == 0)
            {
                continue;
            }
            if (line.Length < 67 || line[64..66] != "  " || !line[..64].All(Uri.IsHexDigit))
            {
                throw CorruptHistory();
            }
            var relative = line[66..];
            if (!IsCanonicalFile(relative) || relative == "checksums.sha256" || !checksums.TryAdd(relative, line[..64].ToLowerInvariant()))
            {
                throw CorruptHistory();
            }
        }
        var files = CanonicalFiles(directory).ToArray();
        if (files.Length != checksums.Count || files.Any(file => !checksums.ContainsKey(file.Destination)))
        {
            throw CorruptHistory();
        }
        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();
            await using var stream = File.OpenRead(file.Path);
            var actual = Convert.ToHexString(await SHA256.HashDataAsync(stream, ct)).ToLowerInvariant();
            publicationCheckpoint?.Invoke(new(id, $"hash:{file.Destination}", false));
            if (actual != checksums[file.Destination])
            {
                throw CorruptHistory();
            }
        }
        return checksums;
    }

    private async Task AcceptPublicationMetadataAsync(string directory, RunManifest manifest, IReadOnlyDictionary<string, string> checksums, IReadOnlyList<JournalFile> committedFiles)
    {
        var index = PublicationStripeIndex(manifest.Id);
        var cached = publicationMetadata[index];
        if (cached?.Directory != directory)
        {
            await GetPublicationMetadataAsync(directory, manifest, CancellationToken.None);
            return;
        }
        // Only the stripe owner mutates these private locator indexes. Updating
        // them does not copy every historical identity for each append.
        var plans = cached.PlanIdentities;
        var events = cached.EventIdentities;
        foreach (var file in committedFiles)
        {
            if (file.Destination.StartsWith("plans/", StringComparison.Ordinal) && file.Destination.EndsWith(".event-id", StringComparison.Ordinal))
            {
                var receipt = await ReadJson<PlanIdentity>(Path.Combine(directory, file.Destination));
                plans[receipt.EventId] = file.Destination;
            }
            if (file.Destination.StartsWith("events/", StringComparison.Ordinal) && file.Destination.EndsWith(".json", StringComparison.Ordinal))
            {
                var value = await ReadJson<RunEvent>(Path.Combine(directory, file.Destination));
                events[value.EventId] = new(file.Destination);
            }
        }
        var inventory = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var entry in checksums)
        {
            inventory.Add(entry.Key, entry.Value);
        }
        publicationMetadata[index] = new(directory, manifest.Sequence, inventory, plans, events);
    }

    private async Task<IReadOnlyList<RunEvent>> ReadEventsAsync(string directory, CancellationToken ct)
    {
        var events = new List<RunEvent>();
        await foreach (var entry in StoredEventsAsync(directory, ct))
        {
            events.Add(entry.Event);
        }
        return events.OrderBy(value => value.Sequence).ToArray();
    }

    private async Task<RunEvent> ReadPublishedEventAsync(string directory, EventLocator locator, CancellationToken ct)
    {
        if (locator.LegacyLine < 0)
        {
            return await ReadJson<RunEvent>(Path.Combine(directory, locator.RelativePath), ct);
        }
        long index = 0;
        await foreach (var line in File.ReadLinesAsync(Path.Combine(directory, locator.RelativePath), ct))
        {
            if (index++ == locator.LegacyLine)
            {
                return DeserializeStoredEvent(line);
            }
        }
        throw CorruptHistory();
    }

    private async IAsyncEnumerable<(RunEvent Event, EventLocator Locator)> StoredEventsAsync(string directory, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        var legacy = Path.Combine(directory, "events.jsonl");
        if (File.Exists(legacy))
        {
            long lineIndex = 0;
            await foreach (var line in File.ReadLinesAsync(legacy, ct))
            {
                var locator = new EventLocator("events.jsonl", lineIndex++);
                if (line.Length != 0)
                {
                    yield return (DeserializeStoredEvent(line), locator);
                }
            }
        }
        var segments = Path.Combine(directory, "events");
        if (Directory.Exists(segments))
        {
            foreach (var file in Directory.GetFiles(segments, "*.json").OrderBy(PlanSequence))
            {
                var value = await ReadJson<RunEvent>(file, ct);
                ValidateStoredEvent(value);
                if (value.Sequence != PlanSequence(file))
                {
                    throw CorruptHistory();
                }
                yield return (value, new(Path.GetRelativePath(directory, file).Replace('\\', '/')));
            }
        }
    }

    private static RunEvent DeserializeStoredEvent(string line)
    {
        try
        {
            var value = JsonSerializer.Deserialize<RunEvent>(line, lineJson) ?? throw CorruptHistory();
            ValidateStoredEvent(value);
            return value;
        }
        catch (JsonException)
        {
            throw CorruptHistory();
        }
    }

    private static void ValidateStoredEvent(RunEvent value)
    {
        if (value.Sequence <= 0 || string.IsNullOrWhiteSpace(value.EventId) || value.EventId.Length > 128
            || string.IsNullOrWhiteSpace(value.Kind) || value.Kind.Length > 128)
        {
            throw CorruptHistory();
        }
    }

    private static EnochException CorruptHistory() => new("storage_corrupt", "Run checksums or publication history are inconsistent; preserve the bundle for operator repair.", 500);
}
