using System.Text;
using Enoch.Protocol;
using Enoch.Storage.FileSystem;
using Xunit;

namespace Enoch.Backend.Tests;

public sealed class TerminalPublicationTests
{
    [Fact]
    public async Task Checksum_failure_before_finish_preserves_active_state_and_retry_is_immutable()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        using var store = new FileSystemRunStore(root);
        await store.StartAsync(new("finish", true, "finish"));
        var checksums = Path.Combine(root, "runs", "finish", "checksums.sha256");
        var originalChecksums = await File.ReadAllTextAsync(checksums);
        File.Delete(checksums);
        Directory.CreateDirectory(checksums);
        await Assert.ThrowsAnyAsync<IOException>(() => store.FinishAsync("finish", new(RunOutcome.Partial, "summary")));
        Assert.Equal(RunState.Running, (await store.GetAsync("finish")).Manifest.State);
        Directory.Delete(checksums);
        await File.WriteAllTextAsync(checksums, originalChecksums);
        var first = await store.FinishAsync("finish", new(RunOutcome.Partial, "summary"));
        var before = await store.GetAsync("finish");
        var digest = await File.ReadAllTextAsync(checksums);
        Assert.Equal(first, await store.FinishAsync("finish", new(RunOutcome.Partial, "summary")));
        Assert.Equal(before.Manifest, (await store.GetAsync("finish")).Manifest);
        Assert.Equal(digest, await File.ReadAllTextAsync(checksums));
        Assert.Equal("run_finished", (await Assert.ThrowsAsync<EnochException>(() => store.FinishAsync("finish", new(RunOutcome.Failed, "changed")))).Code);
        Assert.Equal("run_finished", (await Assert.ThrowsAsync<EnochException>(() => store.FinishAsync("finish", new(RunOutcome.Partial, "changed")))).Code);
        Assert.Equal(before.Manifest, (await store.GetAsync("finish")).Manifest);
        Assert.Equal(digest, await File.ReadAllTextAsync(checksums));
    }

    [Fact]
    public async Task Finish_summary_retry_preserves_null_and_empty_distinction()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        using var store = new FileSystemRunStore(root);
        await store.StartAsync(new("summary", true, "summary"));
        var response = await store.FinishAsync("summary", new(RunOutcome.Success));
        var before = (await store.GetAsync("summary")).Manifest;
        Assert.Equal("run_finished", (await Assert.ThrowsAsync<EnochException>(() => store.FinishAsync("summary", new(RunOutcome.Success, "")))).Code);
        Assert.Equal(response, await store.FinishAsync("summary", new(RunOutcome.Success)));
        Assert.Equal(before, (await store.GetAsync("summary")).Manifest);
    }

    [Fact]
    public async Task Interrupted_committed_finish_recovers_original_outcome_and_retry_response()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        using var interrupted = new FileSystemRunStore(root, null, checkpoint =>
        {
            if (checkpoint.Destination == "manifest.json")
            {
                throw new IOException("Simulated interruption after committed terminal manifest publication.");
            }
        });
        await interrupted.StartAsync(new("finish", true, "finish"));
        Assert.Equal("publication_pending", (await Assert.ThrowsAsync<EnochException>(() => interrupted.FinishAsync("finish", new(RunOutcome.Failed, "original")))).Code);
        interrupted.Dispose();
        using var recovered = new FileSystemRunStore(root);
        var run = await recovered.GetAsync("finish");
        Assert.Equal(RunState.Finished, run.Manifest.State);
        Assert.Equal(RunOutcome.Failed, run.Manifest.Outcome);
        Assert.Equal("original", run.Manifest.Summary);
        var retry = await recovered.FinishAsync("finish", new(RunOutcome.Failed, "original"));
        Assert.Equal(run.Manifest.Sequence, retry.Sequence);
        Assert.Equal(run.Manifest, (await recovered.GetAsync("finish")).Manifest);
    }

    [Theory]
    [InlineData("evidence")]
    [InlineData("artifact")]
    [InlineData("result")]
    [InlineData("wait")]
    public async Task Checksum_destination_fault_rejects_remaining_mutations_without_publishing_data(string publication)
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        using var store = new FileSystemRunStore(root);
        await store.StartAsync(new("publication", true, "publication"));
        var checksums = Path.Combine(root, "runs", "publication", "checksums.sha256");
        File.Delete(checksums);
        Directory.CreateDirectory(checksums);
        var before = await store.GetAsync("publication");
        await Assert.ThrowsAnyAsync<IOException>(() => Publish(store, publication));
        var after = await store.GetAsync("publication");
        Assert.Equal(before.Manifest, after.Manifest);
        Assert.Empty(after.Evidence);
        Assert.Empty(after.Artifacts);
        Assert.Null(after.Result);
    }

    [Theory]
    [InlineData("evidence")]
    [InlineData("artifact")]
    [InlineData("result")]
    [InlineData("wait")]
    public async Task Remaining_mutations_recover_their_actual_committed_intent_after_restart(string publication)
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        using var interrupted = new FileSystemRunStore(root, null, checkpoint =>
        {
            if (checkpoint.Destination == ".publication-pending")
            {
                throw new IOException("Simulated interruption at the actual commit decision.");
            }
        });
        await interrupted.StartAsync(new("publication", true, "publication"));
        Assert.Equal("publication_pending", (await Assert.ThrowsAsync<EnochException>(() => Publish(interrupted, publication))).Code);
        interrupted.Dispose();
        using var recovered = new FileSystemRunStore(root);
        var run = await recovered.GetAsync("publication");
        switch (publication)
        {
            case "evidence":
                var evidence = Assert.Single(run.Evidence);
                Assert.Equal(evidence.CreatedAt, run.Manifest.UpdatedAt);
                var body = await recovered.ReadEvidenceAsync("publication", evidence.Id);
                await using (body.Content)
                {
                    using var reader = new StreamReader(body.Content);
                    Assert.Equal("published", await reader.ReadToEndAsync());
                }
                break;
            case "artifact":
                var artifact = Assert.Single(run.Artifacts);
                Assert.Equal(artifact.CreatedAt, run.Manifest.UpdatedAt);
                Assert.Equal(Encoding.UTF8.GetBytes("published"), await File.ReadAllBytesAsync(Path.Combine(root, "runs", "publication", "artifacts", artifact.Id, "file")));
                break;
            case "result":
                Assert.Equal("published", run.Result?.ToString());
                break;
            default:
                Assert.Equal(RunState.Waiting, run.Manifest.State);
                Assert.Equal(RunState.Running, (await recovered.ResumeAsync("publication")).Manifest.State);
                break;
        }
        await recovered.FinishAsync("publication", new(RunOutcome.Success));
        Assert.Equal(RunState.Finished, (await recovered.GetAsync("publication")).Manifest.State);
    }

    private static async Task Publish(FileSystemRunStore store, string publication)
    {
        switch (publication)
        {
            case "evidence":
                await store.AddEvidenceAsync("publication", new("notes", "published"));
                break;
            case "artifact":
                await using (var content = new MemoryStream(Encoding.UTF8.GetBytes("published")))
                {
                    await store.AddArtifactAsync("publication", "output.txt", "text/plain", content);
                }
                break;
            case "result":
                await store.PublishResultAsync("publication", new("published"));
                break;
            default:
                await store.WaitAsync("publication");
                break;
        }
    }
}
