using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Enoch.Protocol;
using Enoch.Storage.FileSystem;
using Xunit;

namespace Enoch.Backend.Tests;

public sealed class HistoryCompatibilityTests
{
    private static readonly JsonSerializerOptions Json = CreateJsonOptions();

    [Fact]
    public async Task Valid_legacy_events_remain_readable_and_new_publications_do_not_rewrite_the_log()
    {
        var root = await LegacyBundle(2);
        var log = Path.Combine(root, "runs", "legacy", "events.jsonl");
        var original = await File.ReadAllBytesAsync(log);
        using var store = new FileSystemRunStore(root);
        var loaded = await store.GetAsync("legacy");
        Assert.Equal(2, loaded.Manifest.Sequence);
        Assert.Single(loaded.Plans);
        Assert.Single(loaded.Events);
        var next = await store.PublishEventAsync("legacy", new("next", "progress", new { value = "next" }));
        Assert.Equal(3, next.Sequence);
        Assert.Equal(original, await File.ReadAllBytesAsync(log));
        Assert.Single(Directory.GetFiles(Path.Combine(root, "runs", "legacy", "events"), "*.json"));
        Assert.Equal(2, (await store.PublishEventAsync("legacy", new("legacy-event", "changed retry", null, -1))).Sequence);
        Assert.Equal("sequence_conflict", (await Assert.ThrowsAsync<EnochException>(() => store.PublishEventAsync("legacy", new("stale", "progress", null, 2)))).Code);
        loaded = await store.GetAsync("legacy");
        Assert.Equal(new long[] { 2, 3 }, loaded.Events.Select(value => value.Sequence));
        Assert.Contains("original", loaded.Events[0].Data?.ToString());
        Assert.Contains("next", loaded.Events[1].Data?.ToString());
    }

    [Theory]
    [InlineData("behind")]
    [InlineData("ahead")]
    [InlineData("duplicate")]
    [InlineData("finished")]
    public async Task Legacy_history_mismatch_without_authentic_intent_is_rejected_without_repair(string mismatch)
    {
        var root = await LegacyBundle(mismatch == "ahead" ? 3 : mismatch == "duplicate" ? 2 : 1, mismatch == "duplicate", mismatch == "finished");
        var directory = Path.Combine(root, "runs", "legacy");
        var before = await FileSnapshot(directory);
        using var store = new FileSystemRunStore(root);
        Assert.Equal("storage_corrupt", (await Assert.ThrowsAsync<EnochException>(() => store.GetAsync("legacy"))).Code);
        Assert.Equal("storage_corrupt", (await Assert.ThrowsAsync<EnochException>(() => store.PublishEventAsync("legacy", new("new-event", "progress")))).Code);
        Assert.Equal("storage_corrupt", (await Assert.ThrowsAsync<EnochException>(() => store.PublishPlanAsync("legacy", new(new { step = "new" }, "new-plan")))).Code);
        Assert.Equal(before, await FileSnapshot(directory));
    }

    [Fact]
    public async Task Event_identity_does_not_retain_caller_payload_or_returned_history_list()
    {
        using var store = new FileSystemRunStore(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));
        await store.StartAsync(new("owned snapshots", true, "snapshots"));
        var data = new Dictionary<string, string> { ["value"] = "original" };
        var published = await store.PublishEventAsync("snapshots", new("identity", "progress", data));
        data["value"] = "caller mutation";
        Assert.Contains("original", JsonSerializer.Serialize(published.Data));
        var loaded = await store.GetAsync("snapshots");
        if (loaded.Events is RunEvent[] mutableArray)
        {
            mutableArray[0] = mutableArray[0] with { Data = new { value = "reader mutation" } };
        }
        else if (loaded.Events is IList<RunEvent> { IsReadOnly: false } mutableList)
        {
            mutableList.Clear();
        }
        var retry = await store.PublishEventAsync("snapshots", new("identity", "different retry"));
        Assert.Equal(1, retry.Sequence);
        Assert.Contains("original", JsonSerializer.Serialize(retry.Data));
        Assert.Single((await store.GetAsync("snapshots")).Events);
    }

    private static async Task<string> LegacyBundle(long sequence, bool duplicate = false, bool finished = false)
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        RunManifest manifest;
        using (var creator = new FileSystemRunStore(root))
        {
            manifest = (await creator.StartAsync(new("legacy", true, "legacy"))).Manifest;
        }
        var directory = Path.Combine(root, "runs", "legacy");
        await File.WriteAllTextAsync(Path.Combine(directory, "plans", "0001.json"), "{\"step\":\"legacy\"}");
        await File.WriteAllTextAsync(Path.Combine(directory, "events.jsonl"), JsonSerializer.Serialize(new RunEvent(duplicate ? 1 : 2, "legacy-event", "progress", new { value = "original" }, manifest.CreatedAt), Json) + "\n");
        manifest = manifest with { Sequence = sequence, State = finished ? RunState.Finished : RunState.Running, Outcome = finished ? RunOutcome.Success : null, FinishedAt = finished ? manifest.CreatedAt : null };
        await File.WriteAllTextAsync(Path.Combine(directory, "manifest.json"), JsonSerializer.Serialize(manifest, Json));
        var checksums = new List<string>();
        foreach (var file in Directory.GetFiles(directory, "*", SearchOption.AllDirectories).Where(path => Path.GetFileName(path) != "checksums.sha256"))
        {
            checksums.Add($"{Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(file))).ToLowerInvariant()}  {Path.GetRelativePath(directory, file).Replace('\\', '/')}");
        }
        checksums.Sort(StringComparer.Ordinal);
        await File.WriteAllTextAsync(Path.Combine(directory, "checksums.sha256"), string.Join("\n", checksums) + "\n");
        return root;
    }

    private static async Task<string[]> FileSnapshot(string directory)
    {
        var files = new List<string>();
        foreach (var file in Directory.GetFiles(directory, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            files.Add($"{Path.GetRelativePath(directory, file)}:{Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(file)))}");
        }
        return files.ToArray();
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }
}
