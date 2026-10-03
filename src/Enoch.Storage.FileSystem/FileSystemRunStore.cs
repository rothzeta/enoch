using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Enoch.Application;
using Enoch.Protocol;

namespace Enoch.Storage.FileSystem;

public sealed class FileSystemRunStore : IRunStore
{
    const long MaxUpload = 64 * 1024 * 1024;
    static readonly Regex IdPattern = new("^[a-zA-Z0-9][a-zA-Z0-9_-]{0,63}$", RegexOptions.Compiled);
    readonly string root;
    readonly ConcurrentDictionary<string, SemaphoreSlim> locks = new();
    readonly JsonSerializerOptions json = new(JsonSerializerDefaults.Web) { WriteIndented = true, Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) } };
    static readonly JsonSerializerOptions lineJson = new(JsonSerializerDefaults.Web);
    public FileSystemRunStore(string root)
    {
        this.root = Path.GetFullPath(root);
        Directory.CreateDirectory(this.root);
    }
    string Dir(string id)
    {
        ValidateId(id);
        return Path.Combine(root, "runs", id);
    }
    static void ValidateId(string id)
    {
        if (!IdPattern.IsMatch(id))
        {
            throw new EnochException("invalid_id", "Run id is invalid.");
        }
    }
    SemaphoreSlim Gate(string id) => locks.GetOrAdd(id, _ => new SemaphoreSlim(1, 1));
    async Task<T> Locked<T>(string id, Func<string, Task<T>> f)
    {
        var g = Gate(id);
        await g.WaitAsync();
        try
        {
            return await f(Dir(id));
        }
        finally
        {
            g.Release();
        }
    }
    static void EnsureActive(RunManifest m)
    {
        if (m.State == RunState.Finished)
        {
            throw new EnochException("run_finished", "Finished runs are immutable.", 409);
        }
    }
    async Task<RunDocument> Read(string d)
    {
        if (!Directory.Exists(d))
        {
            throw new EnochException("not_found", "Run was not found.", 404);
        }

        var m = await ReadJson<RunManifest>(Path.Combine(d, "manifest.json"));
        var req = await ReadJson<object>(Path.Combine(d, "request.json"));
        var plans = Directory.Exists(Path.Combine(d, "plans")) ? Directory.GetFiles(Path.Combine(d, "plans"), "*.json").OrderBy(x => x).Select(x => ReadJson<object>(x).GetAwaiter().GetResult()).ToList() : new();
        var ev = File.Exists(Path.Combine(d, "events.jsonl")) ? (await File.ReadAllLinesAsync(Path.Combine(d, "events.jsonl"))).Where(x => x.Length > 0).Select(x => JsonSerializer.Deserialize<RunEvent>(x, lineJson)!).ToList() : new();
        object? result = File.Exists(Path.Combine(d, "result.json")) ? await ReadJson<object>(Path.Combine(d, "result.json")) : null;
        var evidence = ReadInfos<EvidenceInfo>(Path.Combine(d, "evidence"));
        var artifacts = ReadInfos<ArtifactInfo>(Path.Combine(d, "artifacts"));
        return new(m, req, plans, ev, result, evidence, artifacts);
    }
    static List<T> ReadInfos<T>(string dir) => Directory.Exists(dir) ? Directory.GetFiles(dir, "metadata.json", SearchOption.AllDirectories).Select(x => JsonSerializer.Deserialize<T>(File.ReadAllText(x), lineJson)!).ToList() : new();
    async Task<T> ReadJson<T>(string p)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(await File.ReadAllTextAsync(p), json) ?? throw new JsonException();
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
    async Task WriteAtomic<T>(string p, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        var tmp = p + "." + Guid.NewGuid().ToString("N") + ".tmp";
        await File.WriteAllTextAsync(tmp, JsonSerializer.Serialize(value, json));
        File.Move(tmp, p, true);
    }
    static async Task WriteTextAtomic(string p, string value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        var tmp = p + "." + Guid.NewGuid().ToString("N") + ".tmp";
        await File.WriteAllTextAsync(tmp, value);
        File.Move(tmp, p, true);
    }
    static async Task UpdateChecksums(string d)
    {
        var lines = new List<string>();
        foreach (var p in Directory.GetFiles(d, "*", SearchOption.AllDirectories).Where(x => !x.EndsWith("checksums.sha256", StringComparison.Ordinal)))
        {
            await using var f = File.OpenRead(p);
            lines.Add($"{Sha(f)}  {Path.GetRelativePath(d, p).Replace('\\', '/')}");
        }
        lines.Sort(StringComparer.Ordinal);
        await WriteTextAtomic(Path.Combine(d, "checksums.sha256"), string.Join("\n", lines) + "\n");
    }
    static string Sha(Stream s)
    {
        using var h = SHA256.Create();
        return Convert.ToHexString(h.ComputeHash(s)).ToLowerInvariant();
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
        if (string.IsNullOrWhiteSpace(request.Title) || request.Title.Length > 256)
        {
            throw new EnochException("invalid_title", "Title is required and must be <=256 characters.");
        }

        var id = request.RunId ?? $"{DateTimeOffset.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}";
        ValidateId(id);
        var d = Dir(id);
        var g = Gate(id);
        await g.WaitAsync(ct);
        try
        {
            if (Directory.Exists(d))
            {
                throw new EnochException("already_exists", "Run already exists.", 409);
            }

            Directory.CreateDirectory(Path.Combine(d, "plans"));
            Directory.CreateDirectory(Path.Combine(d, "evidence"));
            Directory.CreateDirectory(Path.Combine(d, "artifacts"));
            var now = DateTimeOffset.UtcNow;
            var m = new RunManifest(id, request.Title, RunState.Running, null, 0, now, now, null);
            await WriteAtomic(Path.Combine(d, "manifest.json"), m);
            await WriteAtomic(Path.Combine(d, "request.json"), request.Request);
            await UpdateChecksums(d);
            return new(m, request.Request, [], [], null, [], []);
        }
        finally
        {
            g.Release();
        }
    }
    public Task<RunDocument> GetAsync(string id, CancellationToken ct = default) => Locked(id, Read);
    public Task<IReadOnlyList<RunManifest>> ListAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<RunManifest>>(Directory.Exists(Path.Combine(root, "runs")) ? Directory.GetDirectories(Path.Combine(root, "runs")).Select(x => ReadJson<RunManifest>(Path.Combine(x, "manifest.json")).GetAwaiter().GetResult()).OrderByDescending(x => x.CreatedAt).ToList() : new());
    async Task<(string, RunManifest)> Active(string id)
    {
        var d = Dir(id);
        var m = await ReadJson<RunManifest>(Path.Combine(d, "manifest.json"));
        EnsureActive(m);
        return (d, m);
    }
    public Task<PublicationResponse> PublishPlanAsync(string id, PlanRequest request, CancellationToken ct = default) => Locked(id, async d =>
    {
        var (_, m) = await Active(id);
        var n = m.Sequence + 1;
        await WriteAtomic(Path.Combine(d, "plans", $"{n:0000}.json"), request.Plan);
        var nm = m with
        {
            Sequence = n,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        await WriteAtomic(Path.Combine(d, "manifest.json"), nm);
        await UpdateChecksums(d);
        return new PublicationResponse(id, n, nm.State);
    });

    public Task<RunEvent> PublishEventAsync(string id, EventRequest request, CancellationToken ct = default) => Locked(id, async d =>
    {
        var (_, m) = await Active(id);
        if (string.IsNullOrWhiteSpace(request.EventId) || request.EventId.Length > 128)
        {
            throw new EnochException("invalid_event_id", "Event id is required and must be <=128 characters.");
        }

        if (string.IsNullOrWhiteSpace(request.Kind) || request.Kind.Length > 128)
        {
            throw new EnochException("invalid_event_kind", "Event kind is required and must be <=128 characters.");
        }

        var ep = Path.Combine(d, "events.jsonl");
        var lines = File.Exists(ep) ? (await File.ReadAllLinesAsync(ep)).Where(x => x.Length > 0).ToList() : new();
        foreach (var line in lines)
        {
            var old = JsonSerializer.Deserialize<RunEvent>(line, lineJson);
            if (old?.EventId == request.EventId)
            {
                return old;
            }
        }
        if (request.ExpectedSequence is not null && request.ExpectedSequence.Value != m.Sequence)
        {
            throw new EnochException("sequence_conflict", "Expected sequence does not match.", 409, new
            {
                actual = m.Sequence
            });
        }

        var e = new RunEvent(m.Sequence + 1, request.EventId, request.Kind, request.Data, request.OccurredAt ?? DateTimeOffset.UtcNow);
        lines.Add(JsonSerializer.Serialize(e, lineJson));
        await WriteTextAtomic(ep, string.Join("\n", lines) + "\n");
        await WriteAtomic(Path.Combine(d, "manifest.json"), m with
        {
            Sequence = e.Sequence,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await UpdateChecksums(d);
        return e;
    });
    public Task<EvidenceInfo> AddEvidenceAsync(string id, EvidenceRequest request, CancellationToken ct = default) => Locked(id, async d =>
    {
        var (_, m) = await Active(id);
        SafeName(request.Name);
        var bytes = Encoding.UTF8.GetBytes(request.Content);
        if (bytes.LongLength > MaxUpload)
        {
            throw new EnochException("too_large", "Evidence exceeds limit.", 413);
        }
        var eid = Guid.NewGuid().ToString("N");
        var ed = Path.Combine(d, "evidence", eid);
        Directory.CreateDirectory(ed);
        var tmp = Path.Combine(ed, "content.tmp");
        try
        {
            await File.WriteAllBytesAsync(tmp, bytes, ct);
            File.Move(tmp, Path.Combine(ed, "content"));
            var i = new EvidenceInfo(eid, request.Name, request.MimeType ?? "text/plain", bytes.LongLength, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), DateTimeOffset.UtcNow);
            await WriteAtomic(Path.Combine(ed, "metadata.json"), i);
            await UpdateChecksums(d);
            return i;
        }
        catch
        {
            try
            {
                if (File.Exists(tmp))
                {
                    File.Delete(tmp);
                }
            }
            catch
            {
            }
            throw;
        }
    });

    public Task<ArtifactInfo> AddArtifactAsync(string id, string name, string mimeType, Stream content, CancellationToken ct = default) => Locked(id, async d =>
    {
        var (_, m) = await Active(id);
        SafeName(name);
        var ad = Path.Combine(d, "artifacts", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(ad);
        var tmp = Path.Combine(ad, "upload.tmp");
        try
        {
            await using (var f = File.Create(tmp))
            {
                await content.CopyToAsync(f, ct);
                if (f.Length > MaxUpload)
                {
                    throw new EnochException("too_large", "Artifact exceeds limit.", 413);
                }
            }
            await using var read = File.OpenRead(tmp);
            var i = new ArtifactInfo(Path.GetFileName(ad), name, string.IsNullOrWhiteSpace(mimeType) ? "application/octet-stream" : mimeType, read.Length, Sha(read), DateTimeOffset.UtcNow);
            File.Move(tmp, Path.Combine(ad, "file"));
            await WriteAtomic(Path.Combine(ad, "metadata.json"), i);
            await UpdateChecksums(d);
            return i;
        }
        catch
        {
            try
            {
                if (Directory.Exists(ad))
                {
                    Directory.Delete(ad, true);
                }
            }
            catch
            {
            }
            throw;
        }
    });

    public Task<PublicationResponse> PublishResultAsync(string id, ResultRequest request, CancellationToken ct = default) => Locked(id, async d =>
    {
        var (_, m) = await Active(id);
        await WriteAtomic(Path.Combine(d, "result.json"), request.Result);
        await WriteAtomic(Path.Combine(d, "manifest.json"), m with
        {
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await UpdateChecksums(d);
        return new PublicationResponse(id, m.Sequence, m.State);
    });

    public Task<PublicationResponse> FinishAsync(string id, FinishRequest request, CancellationToken ct = default) => Locked(id, async d =>
    {
        var (_, m) = await Active(id);
        if (request.Outcome is not (RunOutcome.Success or RunOutcome.Partial or RunOutcome.Failed or RunOutcome.Cancelled or RunOutcome.Expired))
        {
            throw new EnochException("invalid_outcome", "Invalid terminal outcome.");
        }
        var n = m with
        {
            State = RunState.Finished,
            Outcome = request.Outcome,
            Summary = request.Summary,
            FinishedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        await WriteAtomic(Path.Combine(d, "manifest.json"), n);
        await UpdateChecksums(d);
        return new PublicationResponse(id, n.Sequence, n.State);
    });

    public Task<RunDocument> WaitAsync(string id, CancellationToken ct = default) => Transition(id, RunState.Waiting, RunState.Running);
    public Task<RunDocument> ResumeAsync(string id, CancellationToken ct = default) => Transition(id, RunState.Running, RunState.Waiting);
    Task<RunDocument> Transition(string id, RunState target, RunState required) => Locked(id, async d =>
    {
        var (_, m) = await Active(id);
        if (m.State != required)
        {
            throw new EnochException("invalid_transition", $"Run must be {required} to transition to {target}.", 409);
        }
        var n = m with
        {
            State = target,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        await WriteAtomic(Path.Combine(d, "manifest.json"), n);
        await UpdateChecksums(d);
        return await Read(d);
    });

}
