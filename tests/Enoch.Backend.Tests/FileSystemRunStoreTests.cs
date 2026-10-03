using System.Text;
using System.Text.Json;
using Enoch.Protocol;
using Enoch.Storage.FileSystem;
using Xunit;

namespace Enoch.Backend.Tests;
public sealed class FileSystemRunStoreTests
{
    [Fact]
    public async Task Creation_failure_does_not_expose_a_partial_run()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        using var store = new FileSystemRunStore(root);
        await store.StartAsync(new("unaffected", true, "unaffected"));
        Directory.Delete(Path.Combine(root, "runs", ".staging"));
        await File.WriteAllTextAsync(Path.Combine(root, "runs", ".staging"), "blocked staging destination");
        await Assert.ThrowsAnyAsync<IOException>(() => store.StartAsync(new("interrupted", true, "interrupted")));
        Assert.False(Directory.Exists(Path.Combine(root, "runs", "interrupted")));
        Assert.Equal("unaffected", Assert.Single(await store.ListAsync()).Id);
        Assert.Equal("unaffected", (await store.GetAsync("unaffected")).Manifest.Id);
    }

    [Theory]
    [InlineData("manifest.json")]
    [InlineData("request.json")]
    public async Task Incomplete_initial_bundles_are_excluded_from_index(string missingFile)
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var warnings = new List<string>();
        using var store = new FileSystemRunStore(root, warnings.Add);
        await store.StartAsync(new("unaffected", true, "unaffected"));
        await store.StartAsync(new("incomplete", true, "incomplete"));
        File.Delete(Path.Combine(root, "runs", "incomplete", missingFile));
        Assert.Equal("unaffected", Assert.Single(await store.ListAsync()).Id);
        Assert.Contains(warnings, warning => warning.Contains("incomplete", StringComparison.Ordinal) && warning.Contains(missingFile, StringComparison.Ordinal));
        var error = await Assert.ThrowsAsync<EnochException>(() => store.GetAsync("incomplete"));
        Assert.Equal("storage_corrupt", error.Code);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Abandoned_creation_stages_are_cleaned_without_deleting_unknown_content(int preparedFiles)
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var abandoned = Path.Combine(root, "runs", ".staging", $"create-{Guid.NewGuid():N}");
        var unknown = Path.Combine(root, "runs", ".staging", "operator-notes");
        Directory.CreateDirectory(abandoned);
        Directory.CreateDirectory(unknown);
        if (preparedFiles >= 1)
        {
            var now = DateTimeOffset.UtcNow;
            File.WriteAllText(Path.Combine(abandoned, "manifest.json"), JsonSerializer.Serialize(new RunManifest("unpublished", "staged", RunState.Running, null, 0, now, now, null)));
        }
        if (preparedFiles >= 2)
        {
            File.WriteAllText(Path.Combine(abandoned, "request.json"), "true");
        }
        if (preparedFiles >= 3)
        {
            var checksums = new[] { "manifest.json", "request.json" }.Select(name => $"{Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(Path.Combine(abandoned, name)))).ToLowerInvariant()}  {name}");
            File.WriteAllText(Path.Combine(abandoned, "checksums.sha256"), string.Join("\n", checksums) + "\n");
        }
        using var store = new FileSystemRunStore(root);
        Assert.False(Directory.Exists(abandoned));
        Assert.True(Directory.Exists(unknown));
    }

    [Fact]
    public void Concurrent_store_owners_are_rejected()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        using var store = new FileSystemRunStore(root);
        var error = Assert.Throws<EnochException>(() => new FileSystemRunStore(root));
        Assert.Equal("store_in_use", error.Code);
        store.Dispose();
        using var restarted = new FileSystemRunStore(root);
    }

    [Fact]
    public async Task Shared_runs_mount_enforces_ownership_across_distinct_data_roots()
    {
        var fixture = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var sharedRuns = Path.Combine(fixture, "shared-runs");
        var firstRoot = Path.Combine(fixture, "first");
        var secondRoot = Path.Combine(fixture, "second");
        Directory.CreateDirectory(sharedRuns);
        Directory.CreateDirectory(firstRoot);
        Directory.CreateDirectory(secondRoot);
        Directory.CreateSymbolicLink(Path.Combine(firstRoot, "runs"), sharedRuns);
        Directory.CreateSymbolicLink(Path.Combine(secondRoot, "runs"), sharedRuns);
        using var first = new FileSystemRunStore(firstRoot);
        var error = Assert.Throws<EnochException>(() => new FileSystemRunStore(secondRoot));
        Assert.Equal("store_in_use", error.Code);
        await first.StartAsync(new("shared", true, "shared"));
        first.Dispose();
        using var second = new FileSystemRunStore(secondRoot);
        Assert.Equal("shared", (await second.GetAsync("shared")).Manifest.Id);
    }

    [Fact]
    public async Task Request_serialization_failure_leaves_no_published_bundle_or_stage()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        using var store = new FileSystemRunStore(root);
        await store.StartAsync(new("unaffected", true, "unaffected"));
        await Assert.ThrowsAsync<JsonException>(() => store.StartAsync(new("invalid", new CyclicRequest(), "invalid")));
        Assert.False(Directory.Exists(Path.Combine(root, "runs", "invalid")));
        Assert.Empty(Directory.GetDirectories(Path.Combine(root, "runs", ".staging")));
        Assert.Equal("unaffected", Assert.Single(await store.ListAsync()).Id);
    }

    [Fact]
    public async Task Initial_bundle_checksums_match_the_published_files()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        using var store = new FileSystemRunStore(root);
        await store.StartAsync(new("valid", new { input = 42 }, "valid"));
        store.Dispose();
        using var recovered = new FileSystemRunStore(root);
        Assert.Equal("valid", (await recovered.GetAsync("valid")).Manifest.Id);
        var bundle = Path.Combine(root, "runs", "valid");
        var entries = await File.ReadAllLinesAsync(Path.Combine(bundle, "checksums.sha256"));
        Assert.Equal(2, entries.Length);
        foreach (var entry in entries)
        {
            var parts = entry.Split("  ", 2, StringSplitOptions.None);
            var content = await File.ReadAllBytesAsync(Path.Combine(bundle, parts[1]));
            Assert.Equal(parts[0], Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(content)).ToLowerInvariant());
        }
        Assert.Empty(Directory.GetDirectories(Path.Combine(root, "runs", ".staging")));
    }

    private sealed record CyclicRequest
    {
        public CyclicRequest Self => this;
    }

    [Fact]
    public async Task Corrupt_complete_manifest_is_not_silently_excluded_from_index()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        using var store = new FileSystemRunStore(root);
        await store.StartAsync(new("corrupt", true, "corrupt"));
        await File.WriteAllTextAsync(Path.Combine(root, "runs", "corrupt", "manifest.json"), "{");
        var error = await Assert.ThrowsAsync<EnochException>(() => store.ListAsync());
        Assert.Equal("storage_corrupt", error.Code);
    }

    [Theory]
    [InlineData("request", "clr")]
    [InlineData("request", "json")]
    [InlineData("request", "undefined")]
    [InlineData("plan", "clr")]
    [InlineData("plan", "json")]
    [InlineData("plan", "undefined")]
    [InlineData("result", "clr")]
    [InlineData("result", "json")]
    [InlineData("result", "undefined")]
    public async Task Null_payloads_are_rejected_at_the_store_boundary(string publication, string representation)
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        using var store = new FileSystemRunStore(root);
        await store.StartAsync(new("valid", true, "valid-run"));
        using var json = JsonDocument.Parse("null");
        object? value = representation switch { "json" => json.RootElement, "undefined" => default(JsonElement), _ => null };
        var before = Directory.GetFiles(root, "*", SearchOption.AllDirectories).Where(path => Path.GetFileName(path) != ".enoch-store.lock").ToDictionary(path => path, File.ReadAllText);
        var error = await Assert.ThrowsAsync<EnochException>(async () =>
        {
            switch (publication)
            {
                case "request":
                    await store.StartAsync(new("invalid", value!, "invalid-run"));
                    break;
                case "plan":
                    await store.PublishPlanAsync("valid-run", new(value!));
                    break;
                default:
                    await store.PublishResultAsync("valid-run", new(value!));
                    break;
            }
        });
        Assert.Equal($"invalid_{publication}", error.Code);
        Assert.Equal(before.Keys.Order(), Directory.GetFiles(root, "*", SearchOption.AllDirectories).Where(path => Path.GetFileName(path) != ".enoch-store.lock").Order());
        foreach (var file in before)
        {
            Assert.Equal(file.Value, await File.ReadAllTextAsync(file.Key));
        }
        store.Dispose();
        using var recovered = new FileSystemRunStore(root);
        Assert.Equal(RunState.Running, (await recovered.GetAsync("valid-run")).Manifest.State);
    }

    [Theory]
    [InlineData(RunOutcome.Success)]
    [InlineData(RunOutcome.Partial)]
    [InlineData(RunOutcome.Failed)]
    [InlineData(RunOutcome.Cancelled)]
    [InlineData(RunOutcome.Expired)]
    public async Task Valid_scalar_payloads_and_explicit_outcomes_remain_readable(RunOutcome outcome)
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        using var store = new FileSystemRunStore(root);
        await store.StartAsync(new("valid", 42, "valid-run"));
        await store.PublishPlanAsync("valid-run", new(false));
        await store.PublishResultAsync("valid-run", new("answer"));
        await store.FinishAsync("valid-run", new(outcome));
        store.Dispose();
        using var recovered = new FileSystemRunStore(root);
        var loaded = await recovered.GetAsync("valid-run");
        Assert.Equal(outcome, loaded.Manifest.Outcome);
        Assert.Equal("42", loaded.Request.ToString());
        Assert.Equal("False", Assert.Single(loaded.Plans).ToString());
        Assert.Equal("answer", loaded.Result?.ToString());
    }

    [Fact]
    public async Task Existing_incomplete_run_remains_storage_corrupt()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        using var store = new FileSystemRunStore(root);
        Directory.CreateDirectory(Path.Combine(root, "runs", "incomplete"));
        var error = await Assert.ThrowsAsync<EnochException>(() => store.PublishPlanAsync("incomplete", new(new { step = "work" })));
        Assert.Equal("storage_corrupt", error.Code);
        Assert.Equal(500, error.Status);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(999)]
    public async Task Missing_or_invalid_store_outcome_leaves_run_unchanged(int? outcome)
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        using var store = new FileSystemRunStore(root);
        await store.StartAsync(new("valid", true, "valid-run"));
        var before = Directory.GetFiles(root, "*", SearchOption.AllDirectories).Where(path => Path.GetFileName(path) != ".enoch-store.lock").ToDictionary(path => path, File.ReadAllText);
        var error = await Assert.ThrowsAsync<EnochException>(() => store.FinishAsync("valid-run", new(outcome.HasValue ? (RunOutcome)outcome.Value : null)));
        Assert.Equal("invalid_outcome", error.Code);
        foreach (var file in before)
        {
            Assert.Equal(file.Value, await File.ReadAllTextAsync(file.Key));
        }
        store.Dispose();
        using var recovered = new FileSystemRunStore(root);
        Assert.Equal(RunState.Running, (await recovered.GetAsync("valid-run")).Manifest.State);
    }

    [Fact]
    public async Task LifecycleAndRecoveryAreDurable()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        using var s = new FileSystemRunStore(root);
        var run = await s.StartAsync(new("demo", new
        {
            prompt = "hello"
        }, "run1"));
        await s.PublishPlanAsync("run1", new(new
        {
            steps = new[] { "work" }
        }));
        await s.PublishEventAsync("run1", new("e1", "progress", new
        {
            percent = 50
        }));
        await s.AddEvidenceAsync("run1", new("notes", "done"));
        await using var a = new MemoryStream(Encoding.UTF8.GetBytes("payload"));
        await s.AddArtifactAsync("run1", "out.txt", "text/plain", a);
        await s.PublishResultAsync("run1", new(new
        {
            answer = "ok"
        }));
        var finished = await s.FinishAsync("run1", new(RunOutcome.Success, "complete"));
        Assert.Equal(RunState.Finished, finished.State);
        s.Dispose();
        using var recovered = new FileSystemRunStore(root);
        var loaded = await recovered.GetAsync("run1");
        Assert.Single(loaded.Artifacts);
        Assert.Single(loaded.Events);
        Assert.NotNull(loaded.Result);
        Assert.Equal("complete", loaded.Manifest.Summary);
        Assert.Contains("ok", loaded.Result!.ToString());
    }
    [Fact]
    public async Task DuplicateEventIsIdempotentAndDoesNotAdvanceSequence()
    {
        using var s = new FileSystemRunStore(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()));
        await s.StartAsync(new("x", new
        {
        }, "run1"));
        var a = await s.PublishEventAsync("run1", new("same", "progress"));
        var b = await s.PublishEventAsync("run1", new("same", "progress", new
        {
            different = true
        }));
        Assert.Equal(a.Sequence, b.Sequence);
        Assert.Equal(a.EventId, b.EventId);
        Assert.Equal(a.Sequence, (await s.GetAsync("run1")).Manifest.Sequence);
    }
    [Fact]
    public async Task FinishedRunRejectsMutation()
    {
        using var s = new FileSystemRunStore(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()));
        await s.StartAsync(new("x", new
        {
        }, "run1"));
        await s.FinishAsync("run1", new(RunOutcome.Failed));
        var ex = await Assert.ThrowsAsync<EnochException>(() => s.PublishEventAsync("run1", new("e", "progress")));
        Assert.Equal("run_finished", ex.Code);
    }
    [Fact]
    public async Task ExpectedSequenceRejectsStaleWriter()
    {
        using var s = new FileSystemRunStore(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()));
        await s.StartAsync(new("x", new
        {
        }, "run1"));
        await s.PublishEventAsync("run1", new("e", "progress", ExpectedSequence: 0));
        var ex = await Assert.ThrowsAsync<EnochException>(() => s.PublishEventAsync("run1", new("e2", "progress", ExpectedSequence: 0)));
        Assert.Equal("sequence_conflict", ex.Code);
    }
    [Fact]
    public async Task WaitResumeAndChecksumsArePersisted()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        using var s = new FileSystemRunStore(root);
        await s.StartAsync(new("x", new
        {
        }, "run1"));
        Assert.Equal(RunState.Waiting, (await s.WaitAsync("run1")).Manifest.State);
        Assert.Equal(RunState.Running, (await s.ResumeAsync("run1")).Manifest.State);
        var checksum = Path.Combine(root, "runs", "run1", "checksums.sha256");
        Assert.True(File.Exists(checksum));
        Assert.Contains("manifest.json", await File.ReadAllTextAsync(checksum));
    }
}
