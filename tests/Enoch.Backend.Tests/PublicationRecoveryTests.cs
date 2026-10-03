using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Enoch.Protocol;
using Enoch.Storage.FileSystem;
using Xunit;

namespace Enoch.Backend.Tests;

public sealed class PublicationRecoveryTests
{
    private static readonly JsonSerializerOptions FixtureJson = new(JsonSerializerDefaults.Web);
    [Theory]
    [InlineData("plan")]
    [InlineData("event")]
    public async Task Interrupted_publication_recovers_its_sequence_content_and_retry_identity(string publication)
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var interruptedFile = publication == "plan" ? "plans/0001.json" : "events/0001.json";
        using var interrupted = new FileSystemRunStore(root, null, checkpoint =>
        {
            if (checkpoint.Destination == interruptedFile)
            {
                throw new IOException("Simulated interruption after an actual publication file write.");
            }
        });
        await interrupted.StartAsync(new("recovery", true, "recoverable"));
        await Assert.ThrowsAnyAsync<Exception>(async () =>
        {
            if (publication == "plan")
            {
                await interrupted.PublishPlanAsync("recoverable", new(new { step = "original" }, "identity"));
            }
            else
            {
                await interrupted.PublishEventAsync("recoverable", new("identity", "progress", new { step = "original" }));
            }
        });
        interrupted.Dispose();
        using var recovered = new FileSystemRunStore(root);
        var loaded = await recovered.GetAsync("recoverable");
        Assert.Equal(1, loaded.Manifest.Sequence);
        if (publication == "plan")
        {
            Assert.Contains("original", Assert.Single(loaded.Plans).ToString());
            Assert.Equal(1, (await recovered.PublishPlanAsync("recoverable", new(new { step = "changed retry" }, "identity"))).Sequence);
            Assert.Equal(2, (await recovered.PublishPlanAsync("recoverable", new(new { step = "second" }, "second"))).Sequence);
            Assert.Equal(2, (await recovered.GetAsync("recoverable")).Plans.Count);
        }
        else
        {
            Assert.Contains("original", Assert.Single(loaded.Events).Data?.ToString());
            Assert.Equal(1, (await recovered.PublishEventAsync("recoverable", new("identity", "changed retry"))).Sequence);
            Assert.Equal(2, (await recovered.PublishEventAsync("recoverable", new("second", "progress"))).Sequence);
            Assert.Equal(2, (await recovered.GetAsync("recoverable")).Events.Count);
        }
        await AssertValidChecksums(root, "recoverable");
    }

    [Fact]
    public async Task Plan_identity_retry_returns_original_publication_without_replacing_content()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        using var store = new FileSystemRunStore(root);
        await store.StartAsync(new("identity", true, "identity"));
        var first = await store.PublishPlanAsync("identity", new(new { step = "original" }, "identity"));
        var retry = await store.PublishPlanAsync("identity", new(new { step = "different" }, "identity"));
        Assert.Equal(first, retry);
        var run = await store.GetAsync("identity");
        Assert.Equal(1, run.Manifest.Sequence);
        Assert.Contains("original", Assert.Single(run.Plans).ToString());
    }

    [Fact]
    public async Task Plan_revisions_are_read_in_numeric_order_across_four_digit_boundary()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        using var store = new FileSystemRunStore(root);
        await store.StartAsync(new("ordering", true, "ordering"));
        var plans = Path.Combine(root, "runs", "ordering", "plans");
        await File.WriteAllTextAsync(Path.Combine(plans, "9999.json"), "\"first\"");
        await File.WriteAllTextAsync(Path.Combine(plans, "10000.json"), "\"second\"");
        var directory = Path.GetDirectoryName(plans)!;
        var manifestPath = Path.Combine(directory, "manifest.json");
        var manifest = JsonNode.Parse(await File.ReadAllTextAsync(manifestPath))!;
        manifest["sequence"] = 10000;
        await File.WriteAllTextAsync(manifestPath, manifest.ToJsonString());
        var events = Enumerable.Range(1, 9998).Select(sequence => JsonSerializer.Serialize(new RunEvent(sequence, $"legacy-{sequence}", "progress", null, DateTimeOffset.UnixEpoch), FixtureJson));
        await File.WriteAllLinesAsync(Path.Combine(directory, "events.jsonl"), events);
        var checksumLines = new List<string>();
        foreach (var path in Directory.GetFiles(directory, "*", SearchOption.AllDirectories).Where(path => Path.GetFileName(path) != "checksums.sha256"))
        {
            checksumLines.Add($"{Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(path))).ToLowerInvariant()}  {Path.GetRelativePath(directory, path).Replace('\\', '/')}");
        }
        await File.WriteAllLinesAsync(Path.Combine(directory, "checksums.sha256"), checksumLines.Order(StringComparer.Ordinal));
        var run = await store.GetAsync("ordering");
        Assert.Equal(new[] { "first", "second" }, run.Plans.Select(plan => plan.ToString()));
    }

    [Theory]
    [InlineData(".publication-prepared", false)]
    [InlineData(".publication-pending", true)]
    [InlineData("plans/0001.event-id", true)]
    [InlineData("checksums.sha256", true)]
    [InlineData("manifest.json", true)]
    [InlineData(".publication-cleanup", true)]
    public async Task Restart_and_retry_converge_after_distinct_actual_commit_boundaries(string destination, bool committed)
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        PublicationCheckpoint? observed = null;
        using var interrupted = new FileSystemRunStore(root, null, checkpoint =>
        {
            if (checkpoint.Destination == destination)
            {
                observed = checkpoint;
                throw new IOException("Simulated interruption at a completed publication checkpoint.");
            }
        });
        await interrupted.StartAsync(new("boundaries", true, "boundaries"));
        await Assert.ThrowsAnyAsync<Exception>(() => interrupted.PublishPlanAsync("boundaries", new(new { step = "original" }, "identity")));
        Assert.Equal(committed, Assert.IsType<PublicationCheckpoint>(observed).CommitDecided);
        interrupted.Dispose();
        using var recovered = new FileSystemRunStore(root);
        Assert.Equal(committed ? 1 : 0, Assert.Single(await recovered.ListAsync()).Sequence);
        var run = await recovered.GetAsync("boundaries");
        Assert.Equal(committed ? 1 : 0, run.Plans.Count);
        Assert.Equal(1, (await recovered.PublishPlanAsync("boundaries", new(new { step = "original" }, "identity"))).Sequence);
        Assert.Contains("original", Assert.Single((await recovered.GetAsync("boundaries")).Plans).ToString());
        Assert.False(Directory.Exists(Path.Combine(root, "runs", "boundaries", ".publication-pending")));
        await AssertValidChecksums(root, "boundaries");
    }

    [Theory]
    [InlineData("path")]
    [InlineData("payload")]
    [InlineData("missing")]
    [InlineData("descriptor")]
    public async Task Damaged_actual_journal_is_preserved_and_blocks_public_reads_and_new_sequences(string damage)
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        using var interrupted = new FileSystemRunStore(root, null, checkpoint =>
        {
            if (checkpoint.Destination == ".publication-pending")
            {
                throw new IOException("Simulated interruption immediately after commit decision.");
            }
        });
        await interrupted.StartAsync(new("corruption", true, "corruption"));
        await Assert.ThrowsAsync<EnochException>(() => interrupted.PublishPlanAsync("corruption", new(new { step = "original" }, "identity")));
        interrupted.Dispose();
        var directory = Path.Combine(root, "runs", "corruption");
        var pending = Path.Combine(directory, ".publication-pending");
        var descriptorPath = Path.Combine(pending, "intent.json");
        var descriptor = JsonNode.Parse(await File.ReadAllTextAsync(descriptorPath))!;
        var first = descriptor["files"]![0]!;
        var payload = Path.Combine(pending, first["payload"]!.GetValue<string>());
        switch (damage)
        {
            case "path":
                first["destination"] = "../escaped";
                var modified = System.Text.Encoding.UTF8.GetBytes(descriptor.ToJsonString());
                await File.WriteAllBytesAsync(descriptorPath, modified);
                await File.WriteAllTextAsync(Path.Combine(pending, "intent.sha256"), Convert.ToHexString(SHA256.HashData(modified)));
                break;
            case "payload":
                await File.WriteAllTextAsync(payload, "corrupted payload bytes");
                break;
            case "missing":
                File.Delete(payload);
                break;
            default:
                await File.WriteAllTextAsync(descriptorPath, "corrupted descriptor bytes");
                break;
        }
        var originalManifest = await File.ReadAllTextAsync(Path.Combine(directory, "manifest.json"));
        using var recovered = new FileSystemRunStore(root);
        Assert.Equal("storage_corrupt", (await Assert.ThrowsAsync<EnochException>(() => recovered.GetAsync("corruption"))).Code);
        Assert.Equal("storage_corrupt", (await Assert.ThrowsAsync<EnochException>(() => recovered.PublishEventAsync("corruption", new("next", "progress")))).Code);
        Assert.True(Directory.Exists(pending));
        Assert.Equal(originalManifest, await File.ReadAllTextAsync(Path.Combine(directory, "manifest.json")));
        Assert.False(File.Exists(Path.Combine(root, "runs", "escaped")));
    }

    [Fact]
    public async Task Concurrent_plan_and_event_share_one_sequence_owner()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        using var store = new FileSystemRunStore(root);
        await store.StartAsync(new("concurrent", true, "concurrent"));
        var plan = store.PublishPlanAsync("concurrent", new(new { step = "plan" }, "plan"));
        var runEvent = store.PublishEventAsync("concurrent", new("event", "progress"));
        await Task.WhenAll(plan, runEvent);
        var publishedPlan = await plan;
        var publishedEvent = await runEvent;
        Assert.Equal(new long[] { 1, 2 }, new[] { publishedPlan.Sequence, publishedEvent.Sequence }.Order());
        var run = await store.GetAsync("concurrent");
        Assert.Equal(2, run.Manifest.Sequence);
        Assert.Single(run.Plans);
        Assert.Single(run.Events);
        await AssertValidChecksums(root, "concurrent");
    }

    [Fact]
    public async Task Recovery_cleans_reachable_canonical_copy_temporaries_and_preserves_unknown_files()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        using var interrupted = new FileSystemRunStore(root, null, checkpoint =>
        {
            if (checkpoint.Destination == ".publication-pending")
            {
                throw new IOException("Simulated interruption after publishing an actual replayable intent.");
            }
        });
        await interrupted.StartAsync(new("temporary recovery", true, "temporary"));
        await Assert.ThrowsAsync<EnochException>(() => interrupted.PublishPlanAsync("temporary", new(new { step = "original" }, "identity")));
        interrupted.Dispose();
        var directory = Path.Combine(root, "runs", "temporary");
        var descriptor = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(directory, ".publication-pending", "intent.json")))!;
        var payload = descriptor["files"]![0]!["payload"]!.GetValue<string>();
        var retained = await File.ReadAllBytesAsync(Path.Combine(directory, ".publication-pending", payload));
        var orphan = Path.Combine(directory, "plans", $"0001.json.{Guid.NewGuid():N}.tmp");
        await File.WriteAllBytesAsync(orphan, retained[..Math.Min(3, retained.Length)]);
        var unknown = Path.Combine(directory, $"operator-notes.{Guid.NewGuid():N}.tmp");
        await File.WriteAllTextAsync(unknown, "preserve operator content");
        using var recovered = new FileSystemRunStore(root);
        Assert.Equal(1, (await recovered.GetAsync("temporary")).Manifest.Sequence);
        Assert.False(File.Exists(orphan));
        Assert.Equal("preserve operator content", await File.ReadAllTextAsync(unknown));
        await AssertValidChecksums(root, "temporary");
    }

    private static async Task AssertValidChecksums(string root, string runId)
    {
        var directory = Path.Combine(root, "runs", runId);
        var entries = await File.ReadAllLinesAsync(Path.Combine(directory, "checksums.sha256"));
        foreach (var entry in entries)
        {
            var parts = entry.Split("  ", 2, StringSplitOptions.None);
            Assert.DoesNotContain(".publication", parts[1], StringComparison.Ordinal);
            var content = await File.ReadAllBytesAsync(Path.Combine(directory, parts[1]));
            Assert.Equal(parts[0], Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant());
        }
    }
}
