using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Enoch.Application;
using Enoch.Protocol;

namespace Enoch.Storage.FileSystem;

public sealed partial class FileSystemRunStore : IRunStore, IDisposable
{
    const long MaxUpload = 64 * 1024 * 1024;
    static readonly Regex IdPattern = new("^[a-zA-Z0-9][a-zA-Z0-9_-]{0,63}$", RegexOptions.Compiled);
    static readonly string[] InitialFiles = ["manifest.json", "request.json"];
    readonly string runsRoot;
    readonly FileStream ownership;
    readonly Action<string> reportWarning;
    readonly Action<PublicationCheckpoint>? publicationCheckpoint;
    bool disposed;
    readonly JsonSerializerOptions json = new(JsonSerializerDefaults.Web) { WriteIndented = true, Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) } };
    static readonly JsonSerializerOptions lineJson = new(JsonSerializerDefaults.Web);
    public FileSystemRunStore(string root, Action<string>? reportWarning = null) : this(root, reportWarning, null)
    {
    }
    internal FileSystemRunStore(string root, Action<string>? reportWarning, Action<PublicationCheckpoint>? publicationCheckpoint)
    {
        this.publicationCheckpoint = publicationCheckpoint;
        runsRoot = Path.Combine(Path.GetFullPath(root), "runs");
        this.reportWarning = reportWarning ?? Console.Error.WriteLine;
        Directory.CreateDirectory(runsRoot);
        try
        {
            ownership = File.Open(Path.Combine(runsRoot, ".enoch-store.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException)
        {
            throw new EnochException("store_in_use", "Another store already owns this data root.", 409);
        }
        try
        {
            CleanupAbandonedCreationStages();
        }
        catch
        {
            ownership.Dispose();
            throw;
        }
    }
    public void Dispose()
    {
        if (!disposed)
        {
            disposed = true;
            ownership.Dispose();
            foreach (var gate in publicationGates)
            {
                gate.Dispose();
            }
        }
    }
    void CleanupAbandonedCreationStages()
    {
        var staging = Path.Combine(runsRoot, ".staging");
        if (!Directory.Exists(staging))
        {
            return;
        }
        foreach (var directory in Directory.GetDirectories(staging, "create-*"))
        {
            var name = Path.GetFileName(directory);
            if (Guid.TryParseExact(name[7..], "N", out _))
            {
                Directory.Delete(directory, true);
                reportWarning($"Removed abandoned creation stage '{name}'.");
            }
        }
    }
    string Dir(string id)
    {
        ValidateId(id);
        return Path.Combine(runsRoot, id);
    }
    static void ValidateId(string id)
    {
        if (!IdPattern.IsMatch(id))
        {
            throw new EnochException("invalid_id", "Run id is invalid.");
        }
    }
    SemaphoreSlim Gate(string id) => PublicationGate(id);
    async Task<T> Locked<T>(string id, Func<string, Task<T>> operation, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var gate = Gate(id);
        await gate.WaitAsync(cancellationToken);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var directory = Dir(id);
            await RecoverPublication(directory);
            cancellationToken.ThrowIfCancellationRequested();
            return await operation(directory);
        }
        finally
        {
            gate.Release();
        }
    }
    static void EnsureActive(RunManifest m)
    {
        if (m.State == RunState.Finished)
        {
            throw new EnochException("run_finished", "Finished runs are immutable.", 409);
        }
    }
    static void ValidatePayload(object? payload, string name)
    {
        if (payload is null || payload is JsonElement { ValueKind: JsonValueKind.Null or JsonValueKind.Undefined })
        {
            throw new EnochException($"invalid_{name}", $"A non-null {name} payload is required.");
        }
    }
    async Task<RunDocument> Read(string d, CancellationToken ct = default)
    {
        if (!Directory.Exists(d))
        {
            throw new EnochException("not_found", "Run was not found.", 404);
        }

        var m = await ReadJson<RunManifest>(Path.Combine(d, "manifest.json"), ct);
        await ValidateHistoryAsync(d, m, ct);
        var req = await ReadJson<object>(Path.Combine(d, "request.json"), ct);
        var plans = new List<object>();
        if (Directory.Exists(Path.Combine(d, "plans")))
        {
            foreach (var path in Directory.GetFiles(Path.Combine(d, "plans"), "*.json").OrderBy(PlanSequence))
            {
                plans.Add(await ReadJson<object>(path, ct));
            }
        }
        var ev = await ReadEventsAsync(d, ct);
        object? result = File.Exists(Path.Combine(d, "result.json")) ? await ReadJson<object>(Path.Combine(d, "result.json"), ct) : null;
        var evidence = await ReadInfos<EvidenceInfo>(Path.Combine(d, "evidence"), ct);
        var artifacts = await ReadInfos<ArtifactInfo>(Path.Combine(d, "artifacts"), ct);
        return new(m, req, plans, ev, result, evidence, artifacts);
    }
    async Task<List<T>> ReadInfos<T>(string directory, CancellationToken ct)
    {
        var metadata = new List<T>();
        if (Directory.Exists(directory))
        {
            foreach (var path in Directory.GetFiles(directory, "metadata.json", SearchOption.AllDirectories))
            {
                ct.ThrowIfCancellationRequested();
                metadata.Add(await ReadJson<T>(path, ct));
            }
        }
        return metadata;
    }
    async Task<T> ReadJson<T>(string p, CancellationToken ct = default)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(await File.ReadAllTextAsync(p, ct), json) ?? throw new JsonException();
        }
        catch (FileNotFoundException)
        {
            throw new EnochException("storage_corrupt", "Run bundle is missing a required file.", 500);
        }
        catch (JsonException)
        {
            throw new EnochException("storage_corrupt", "Run bundle contains invalid JSON.", 500);
        }
    }
    async Task WriteAtomic<T>(string p, T value, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        var tmp = p + "." + Guid.NewGuid().ToString("N") + ".tmp";
        await File.WriteAllTextAsync(tmp, JsonSerializer.Serialize(value, json), ct);
        ct.ThrowIfCancellationRequested();
        File.Move(tmp, p, true);
    }
    static async Task WriteTextAtomic(string p, string value, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        var tmp = p + "." + Guid.NewGuid().ToString("N") + ".tmp";
        await File.WriteAllTextAsync(tmp, value, ct);
        ct.ThrowIfCancellationRequested();
        File.Move(tmp, p, true);
    }
    async Task UpdateChecksums(string d, CancellationToken ct)
    {
        var lines = new List<string>();
        foreach (var p in Directory.GetFiles(d, "*", SearchOption.AllDirectories).Where(x => !x.EndsWith("checksums.sha256", StringComparison.Ordinal)))
        {
            await using var f = File.OpenRead(p);
            var relative = Path.GetRelativePath(d, p).Replace('\\', '/');
            lines.Add($"{Convert.ToHexString(await SHA256.HashDataAsync(f, ct)).ToLowerInvariant()}  {relative}");
            publicationCheckpoint?.Invoke(new(Path.GetFileName(d), $"hash:{relative}", false));
        }
        lines.Sort(StringComparer.Ordinal);
        await WriteTextAtomic(Path.Combine(d, "checksums.sha256"), string.Join("\n", lines) + "\n", ct);
    }
    static string SafeName(string n)
    {
        if (string.IsNullOrWhiteSpace(n) || n.Length > 255 || n.Contains('/') || n.Contains('\\') || n is "." or "..")
        {
            throw new EnochException("invalid_name", "Name is invalid.");
        }

        return n;
    }
    public async Task<RunDocument> StartAsync(StartRunRequest request, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        ValidatePayload(request.Request, "request");
        if (string.IsNullOrWhiteSpace(request.Title) || request.Title.Length > 256)
        {
            throw new EnochException("invalid_title", "Title is required and must be <=256 characters.");
        }

        var id = request.RunId ?? $"{DateTimeOffset.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}";
        ValidateId(id);
        var d = Dir(id);
        var g = Gate(id);
        await g.WaitAsync(ct);
        string? stage = null;
        try
        {
            if (Directory.Exists(d))
            {
                throw new EnochException("already_exists", "Run already exists.", 409);
            }

            stage = Path.Combine(runsRoot, ".staging", $"create-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path.Combine(stage, "plans"));
            Directory.CreateDirectory(Path.Combine(stage, "evidence"));
            Directory.CreateDirectory(Path.Combine(stage, "artifacts"));
            var now = DateTimeOffset.UtcNow;
            var m = new RunManifest(id, request.Title, RunState.Running, null, 0, now, now, null);
            await WriteAtomic(Path.Combine(stage, "manifest.json"), m, ct);
            publicationCheckpoint?.Invoke(new(id, ".creation-manifest", false));
            await WriteAtomic(Path.Combine(stage, "request.json"), request.Request, ct);
            await UpdateChecksums(stage, ct);
            publicationCheckpoint?.Invoke(new(id, ".creation-prepared", false));
            ct.ThrowIfCancellationRequested();
            Directory.CreateDirectory(runsRoot);
            Directory.Move(stage, d);
            return new(m, request.Request, [], [], null, [], []);
        }
        finally
        {
            try
            {
                if (stage is not null && Directory.Exists(stage))
                {
                    try
                    {
                        Directory.Delete(stage, true);
                    }
                    catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                    {
                        // The stage remains private and can be retried by startup cleanup.
                        reportWarning($"Creation stage cleanup failed for '{Path.GetFileName(stage)}': {error.Message}");
                    }
                }
            }
            finally
            {
                g.Release();
            }
        }
    }
    public Task<RunDocument> GetAsync(string id, CancellationToken ct = default) => Locked(id, directory => Read(directory, ct), ct);
    public Task<EvidenceContent> ReadEvidenceAsync(string id, string evidenceId, CancellationToken ct = default) => Locked(id, async directory =>
    {
        ct.ThrowIfCancellationRequested();
        if (!Guid.TryParseExact(evidenceId, "N", out _))
        {
            throw new EnochException("invalid_id", "Evidence id is invalid.");
        }
        var run = await Read(directory, ct);
        var metadata = run.Evidence.FirstOrDefault(evidence => evidence.Id == evidenceId)
            ?? throw new EnochException("not_found", "Evidence was not found.", 404);
        try
        {
            var content = new FileStream(Path.Combine(directory, "evidence", metadata.Id, "content"), FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous);
            return new EvidenceContent(metadata, content);
        }
        catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException)
        {
            throw new EnochException("storage_corrupt", "Evidence content is missing.", 500);
        }
    }, ct);
    public Task<ArtifactContent> ReadArtifactAsync(string id, string artifactId, CancellationToken ct = default) => Locked(id, async directory =>
    {
        ct.ThrowIfCancellationRequested();
        if (!Guid.TryParseExact(artifactId, "N", out _))
        {
            throw new EnochException("invalid_id", "Artifact id is invalid.");
        }
        var run = await Read(directory, ct);
        var metadata = run.Artifacts.FirstOrDefault(artifact => artifact.Id == artifactId)
            ?? throw new EnochException("not_found", "Artifact was not found.", 404);
        try
        {
            var content = new FileStream(Path.Combine(directory, "artifacts", metadata.Id, "file"), FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous);
            return new ArtifactContent(metadata, content);
        }
        catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException)
        {
            throw new EnochException("storage_corrupt", "Artifact content is missing.", 500);
        }
    }, ct);
    public async Task<IReadOnlyList<RunManifest>> ListAsync(CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ct.ThrowIfCancellationRequested();
        var runs = runsRoot;
        var manifests = new List<RunManifest>();
        if (!Directory.Exists(runs))
        {
            return manifests;
        }
        foreach (var directory in Directory.GetDirectories(runs).Where(path => Path.GetFileName(path) != ".staging"))
        {
            ct.ThrowIfCancellationRequested();
            var manifest = await Locked(Path.GetFileName(directory), async path =>
            {
                var missing = InitialFiles.Where(name => !File.Exists(Path.Combine(path, name))).ToArray();
                if (missing.Length > 0)
                {
                    reportWarning($"Excluded incomplete run '{Path.GetFileName(path)}' from discovery; missing {string.Join(", ", missing)}. Repair or quarantine this bundle before retrying.");
                    return null;
                }
                return await ReadJson<RunManifest>(Path.Combine(path, "manifest.json"), ct);
            }, ct);
            if (manifest is not null)
            {
                manifests.Add(manifest);
            }
        }
        return manifests.OrderByDescending(manifest => manifest.CreatedAt).ToList();
    }
    async Task<(string, RunManifest)> Existing(string id, CancellationToken ct)
    {
        var d = Dir(id);
        if (!Directory.Exists(d))
        {
            throw new EnochException("not_found", "Run was not found.", 404);
        }
        var m = await ReadJson<RunManifest>(Path.Combine(d, "manifest.json"), ct);
        await ValidateHistoryAsync(d, m, ct);
        return (d, m);
    }
    async Task<(string, RunManifest)> Active(string id, CancellationToken ct)
    {
        var existing = await Existing(id, ct);
        EnsureActive(existing.Item2);
        return existing;
    }
    public Task<PublicationResponse> PublishPlanAsync(string id, PlanRequest request, CancellationToken ct = default) => Locked(id, async d =>
    {
        var (_, m) = await Active(id, ct);
        ValidatePayload(request.Plan, "plan");
        if (request.EventId is not null)
        {
            ValidateEventIdentity(request.EventId);
            var metadata = await GetPublicationMetadataAsync(d, m, ct);
            if (metadata.PlanIdentities.TryGetValue(request.EventId, out var identityPath))
            {
                return (await ReadJson<PlanIdentity>(Path.Combine(d, identityPath), ct)).Response;
            }
        }
        var n = m.Sequence + 1;
        var nm = m with
        {
            Sequence = n,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        var response = new PublicationResponse(id, n, nm.State);
        var replacements = new Dictionary<string, byte[]>
        {
            [$"plans/{n:0000}.json"] = JsonSerializer.SerializeToUtf8Bytes(request.Plan, json)
        };
        if (request.EventId is not null)
        {
            replacements[$"plans/{n:0000}.event-id"] = JsonSerializer.SerializeToUtf8Bytes(new PlanIdentity(request.EventId, response), json);
        }
        return await CommitPublication(d, nm, replacements, response, ct);
    }, ct);

    public Task<RunEvent> PublishEventAsync(string id, EventRequest request, CancellationToken ct = default) => Locked(id, async d =>
    {
        var (_, m) = await Active(id, ct);
        ValidateEventIdentity(request.EventId);

        if (string.IsNullOrWhiteSpace(request.Kind) || request.Kind.Length > 128)
        {
            throw new EnochException("invalid_event_kind", "Event kind is required and must be <=128 characters.");
        }

        var metadata = await GetPublicationMetadataAsync(d, m, ct);
        if (metadata.EventIdentities.TryGetValue(request.EventId, out var locator))
        {
            return await ReadPublishedEventAsync(d, locator, ct);
        }
        if (request.ExpectedSequence is not null && request.ExpectedSequence.Value != m.Sequence)
        {
            throw new EnochException("sequence_conflict", "Expected sequence does not match.", 409, new
            {
                actual = m.Sequence
            });
        }

        var e = new RunEvent(m.Sequence + 1, request.EventId, request.Kind, request.Data, request.OccurredAt ?? DateTimeOffset.UtcNow);
        var eventBytes = JsonSerializer.SerializeToUtf8Bytes(e, lineJson);
        var snapshot = JsonSerializer.Deserialize<RunEvent>(eventBytes, lineJson)!;
        return await CommitPublication(d, m with
        {
            Sequence = e.Sequence,
            UpdatedAt = DateTimeOffset.UtcNow
        }, new Dictionary<string, byte[]> { [$"events/{e.Sequence:0000}.json"] = eventBytes }, snapshot, ct);
    }, ct);
    public Task<EvidenceInfo> AddEvidenceAsync(string id, EvidenceRequest request, CancellationToken ct = default) => Locked(id, async directory =>
    {
        var (_, manifest) = await Active(id, ct);
        SafeName(request.Name);
        if (request.Content is null)
        {
            throw new EnochException("invalid_evidence", "Evidence content is required.");
        }
        if (Encoding.UTF8.GetByteCount(request.Content) > MaxUpload)
        {
            throw new EnochException("too_large", "Evidence exceeds limit.", 413);
        }
        var content = Encoding.UTF8.GetBytes(request.Content);
        var now = DateTimeOffset.UtcNow;
        var evidence = new EvidenceInfo(Guid.NewGuid().ToString("N"), request.Name, request.MimeType ?? "text/plain", content.LongLength, Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant(), now);
        return await CommitPublication(directory, manifest with { UpdatedAt = now }, new Dictionary<string, byte[]>
        {
            [$"evidence/{evidence.Id}/content"] = content,
            [$"evidence/{evidence.Id}/metadata.json"] = JsonSerializer.SerializeToUtf8Bytes(evidence, json)
        }, evidence, ct);
    }, ct);

    public Task<ArtifactInfo> AddArtifactAsync(string id, string name, string mimeType, Stream content, CancellationToken ct = default) => Locked(id, async directory =>
    {
        var (_, manifest) = await Active(id, ct);
        SafeName(name);
        return await CommitPreparedPublication(directory, async (stage, token) =>
        {
            const string payload = "0000.payload";
            long length;
            string checksum;
            await using (var output = new FileStream(Path.Combine(stage, payload), FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous))
            {
                (length, checksum) = await CopyUploadAsync(content, output, token);
            }
            var now = DateTimeOffset.UtcNow;
            var artifact = new ArtifactInfo(Guid.NewGuid().ToString("N"), name, string.IsNullOrWhiteSpace(mimeType) ? "application/octet-stream" : mimeType, length, checksum, now);
            var files = new List<JournalFile> { new($"artifacts/{artifact.Id}/file", payload, checksum) };
            files.Add(await StagePublicationBytes(stage, $"artifacts/{artifact.Id}/metadata.json", JsonSerializer.SerializeToUtf8Bytes(artifact, json), files.Count, token));
            return new PreparedPublication<ArtifactInfo>(manifest with { UpdatedAt = now }, files, artifact);
        }, ct);
    }, ct);

    public Task<PublicationResponse> PublishResultAsync(string id, ResultRequest request, CancellationToken ct = default) => Locked(id, async directory =>
    {
        var (_, manifest) = await Active(id, ct);
        ValidatePayload(request.Result, "result");
        return await CommitPublication(directory, manifest with { UpdatedAt = DateTimeOffset.UtcNow }, new Dictionary<string, byte[]>
        {
            ["result.json"] = JsonSerializer.SerializeToUtf8Bytes(request.Result, json)
        }, new PublicationResponse(id, manifest.Sequence, manifest.State), ct);
    }, ct);

    public Task<PublicationResponse> FinishAsync(string id, FinishRequest request, CancellationToken ct = default) => Locked(id, async directory =>
    {
        var (_, manifest) = await Existing(id, ct);
        if (request.Outcome is not (RunOutcome.Success or RunOutcome.Partial or RunOutcome.Failed or RunOutcome.Cancelled or RunOutcome.Expired))
        {
            throw new EnochException("invalid_outcome", "Invalid terminal outcome.");
        }
        if (manifest.State == RunState.Finished)
        {
            if (manifest.Outcome == request.Outcome && manifest.Summary == request.Summary)
            {
                return new PublicationResponse(id, manifest.Sequence, manifest.State);
            }
            throw new EnochException("run_finished", "Finished runs are immutable.", 409);
        }
        var now = DateTimeOffset.UtcNow;
        var finished = manifest with { State = RunState.Finished, Outcome = request.Outcome, Summary = request.Summary, FinishedAt = now, UpdatedAt = now };
        return await CommitPublication(directory, finished, new Dictionary<string, byte[]>(), new PublicationResponse(id, finished.Sequence, finished.State), ct);
    }, ct);

    public Task<RunDocument> WaitAsync(string id, CancellationToken ct = default) => Transition(id, RunState.Waiting, RunState.Running, ct);
    public Task<RunDocument> ResumeAsync(string id, CancellationToken ct = default) => Transition(id, RunState.Running, RunState.Waiting, ct);
    Task<RunDocument> Transition(string id, RunState target, RunState required, CancellationToken ct) => Locked(id, async directory =>
    {
        var (_, manifest) = await Active(id, ct);
        if (manifest.State != required)
        {
            throw new EnochException("invalid_transition", $"Run must be {required} to transition to {target}.", 409);
        }
        var current = await Read(directory, ct);
        var next = current with { Manifest = manifest with { State = target, UpdatedAt = DateTimeOffset.UtcNow } };
        return await CommitPublication(directory, next.Manifest, new Dictionary<string, byte[]>(), next, ct);
    }, ct);
}

internal sealed record PublicationCheckpoint(string RunId, string Destination, bool CommitDecided);
