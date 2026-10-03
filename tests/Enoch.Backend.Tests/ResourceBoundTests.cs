using Enoch.Protocol;
using Enoch.Storage.FileSystem;
using Xunit;

namespace Enoch.Backend.Tests;

public sealed class ResourceBoundTests
{
    private const long UploadLimit = 64L * 1024 * 1024;

    [Fact]
    public async Task Unknown_and_invalid_ids_do_not_retain_unbounded_publication_gates()
    {
        var allocated = 0;
        using var store = new FileSystemRunStore(NewRoot(), null, checkpoint =>
        {
            if (checkpoint.Destination == "gate-allocated")
            {
                allocated++;
            }
        });
        await store.StartAsync(new("gates", true, "known"));
        for (var index = 0; index < 200; index++)
        {
            Assert.Equal("not_found", (await Assert.ThrowsAsync<EnochException>(() => store.GetAsync($"missing-{index}"))).Code);
        }
        var beforeInvalid = allocated;
        for (var index = 0; index < 100; index++)
        {
            Assert.Equal("invalid_id", (await Assert.ThrowsAsync<EnochException>(() => store.GetAsync($"../invalid-{index}"))).Code);
        }
        Assert.Equal(beforeInvalid, allocated);
        Assert.InRange(allocated, 0, 64);
        Assert.Equal("known", (await store.GetAsync("known")).Manifest.Id);
    }

    [Fact]
    public async Task Unknown_length_upload_stops_after_limit_plus_one_and_preserves_bundle()
    {
        var root = NewRoot();
        using var store = new FileSystemRunStore(root);
        await store.StartAsync(new("bounded", true, "bounded"));
        var before = await File.ReadAllBytesAsync(Path.Combine(root, "runs", "bounded", "checksums.sha256"));
        using var input = new GeneratedStream(UploadLimit + 1024 * 1024);
        var rejected = await Assert.ThrowsAsync<EnochException>(() => store.AddArtifactAsync("bounded", "large.bin", "application/octet-stream", input));
        Assert.Equal("too_large", rejected.Code);
        Assert.Equal(413, rejected.Status);
        Assert.InRange(input.Consumed, UploadLimit, UploadLimit + 1);
        Assert.Empty((await store.GetAsync("bounded")).Artifacts);
        Assert.Equal(before, await File.ReadAllBytesAsync(Path.Combine(root, "runs", "bounded", "checksums.sha256")));
        Assert.Empty(Directory.EnumerateFiles(Path.Combine(root, "runs", "bounded", "artifacts"), "*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task Already_cancelled_read_does_not_ignore_caller_cancellation()
    {
        using var store = new FileSystemRunStore(NewRoot());
        await store.StartAsync(new("cancelled", true, "cancelled"));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.GetAsync("cancelled", cancellation.Token));
    }

    [Fact]
    public async Task Cancelled_waiter_leaves_inflight_upload_and_sequence_owner_intact()
    {
        using var store = new FileSystemRunStore(NewRoot());
        await store.StartAsync(new("waiter", true, "waiter"));
        using var uploadCancellation = new CancellationTokenSource();
        using var input = new BlockingStream();
        var upload = store.AddArtifactAsync("waiter", "blocked.bin", "application/octet-stream", input, uploadCancellation.Token);
        await input.Entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        using var waiterCancellation = new CancellationTokenSource();
        var waiter = store.GetAsync("waiter", waiterCancellation.Token);
        waiterCancellation.Cancel();
        try
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiter.WaitAsync(TimeSpan.FromSeconds(1)));
        }
        finally
        {
            uploadCancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => upload.WaitAsync(TimeSpan.FromSeconds(2)));
            try
            { await waiter; }
            catch (OperationCanceledException) { }
        }
        var loaded = await store.GetAsync("waiter");
        Assert.Empty(loaded.Artifacts);
        Assert.Equal(1, (await store.PublishEventAsync("waiter", new("after-cancellation", "progress"))).Sequence);
    }

    [Fact]
    public async Task Warm_publications_do_not_hash_unchanged_artifact_bodies_again()
    {
        var historicalReads = 0;
        using var store = new FileSystemRunStore(NewRoot(), null, checkpoint =>
        {
            if (checkpoint.Destination.StartsWith("hash:artifacts/", StringComparison.Ordinal) && checkpoint.Destination.EndsWith("/file", StringComparison.Ordinal))
            {
                historicalReads++;
            }
        });
        await store.StartAsync(new("hashes", true, "hashes"));
        using var input = new GeneratedStream(1024);
        await store.AddArtifactAsync("hashes", "small.bin", "application/octet-stream", input);
        await store.PublishEventAsync("hashes", new("warm", "progress"));
        var afterWarmup = historicalReads;
        await store.PublishEventAsync("hashes", new("next", "progress"));
        await store.PublishPlanAsync("hashes", new(new { step = "next" }, "next-plan"));
        Assert.Equal(afterWarmup, historicalReads);
    }

    [Fact]
    public async Task Warm_owner_retries_original_identity_after_committed_journal_cleanup_failure()
    {
        var failCleanup = false;
        using var store = new FileSystemRunStore(NewRoot(), null, checkpoint =>
        {
            if (failCleanup && checkpoint.Destination == ".publication-cleanup")
            {
                failCleanup = false;
                throw new IOException("Interrupted after canonical commit and journal retirement.");
            }
        });
        await store.StartAsync(new("cleanup", true, "cleanup"));
        await store.PublishEventAsync("cleanup", new("warm", "progress"));
        failCleanup = true;
        await Assert.ThrowsAnyAsync<Exception>(() => store.PublishEventAsync("cleanup", new("interrupted", "progress", new { value = "original" })));
        var retry = await store.PublishEventAsync("cleanup", new("interrupted", "changed retry"));
        Assert.Equal(2, retry.Sequence);
        Assert.Contains("original", retry.Data?.ToString());
        Assert.Equal(3, (await store.PublishEventAsync("cleanup", new("next", "progress"))).Sequence);
        var loaded = await store.GetAsync("cleanup");
        Assert.Equal(3, loaded.Manifest.Sequence);
        Assert.Equal(3, loaded.Events.Count);
    }

    [Theory]
    [InlineData(".publication-prepared", false)]
    [InlineData(".publication-pending", true)]
    public async Task Cancellation_at_publication_decision_preserves_retry_and_sequence(string boundary, bool committed)
    {
        using var cancellation = new CancellationTokenSource();
        using var store = new FileSystemRunStore(NewRoot(), null, checkpoint =>
        {
            if (checkpoint.Destination == boundary)
            {
                cancellation.Cancel();
            }
        });
        await store.StartAsync(new("decision cancellation", true, "decision"));
        var publication = store.PublishPlanAsync("decision", new(new { step = "original" }, "decision-id"), cancellation.Token);
        if (committed)
        {
            Assert.Equal(1, (await publication).Sequence);
        }
        else
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => publication);
        }
        Assert.Equal(committed ? 1 : 0, (await store.GetAsync("decision")).Manifest.Sequence);
        Assert.Equal(1, (await store.PublishPlanAsync("decision", new(new { step = "retry" }, "decision-id"))).Sequence);
        var plan = Assert.Single((await store.GetAsync("decision")).Plans);
        Assert.Contains(committed ? "original" : "retry", plan.ToString());
    }

    private static string NewRoot() => Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

    private class GeneratedStream(long remaining) : Stream
    {
        public long Consumed { get; private set; }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count)
        {
            var read = (int)Math.Min(count, remaining);
            buffer.AsSpan(offset, read).Fill(42);
            remaining -= read;
            Consumed += read;
            return read;
        }
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var read = (int)Math.Min(buffer.Length, remaining);
            buffer.Span[..read].Fill(42);
            remaining -= read;
            Consumed += read;
            return ValueTask.FromResult(read);
        }
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class BlockingStream() : GeneratedStream(0)
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Entered.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return 0;
        }
    }
}
