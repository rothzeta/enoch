using Enoch.Protocol;
using Enoch.Storage.FileSystem;
using Xunit;

namespace Enoch.Backend.Tests;

public sealed class CreationCancellationTests
{
    [Theory]
    [InlineData("before")]
    [InlineData(".creation-manifest")]
    [InlineData(".creation-prepared")]
    public async Task Cancelled_creation_keeps_staged_bundle_private_and_unrelated_run_readable(string boundary)
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        using var cancellation = new CancellationTokenSource();
        using var store = new FileSystemRunStore(root, null, checkpoint =>
        {
            if (checkpoint.RunId == "cancelled" && checkpoint.Destination == boundary)
            {
                cancellation.Cancel();
            }
        });
        await store.StartAsync(new("unrelated", true, "unrelated"));
        if (boundary == "before")
        {
            cancellation.Cancel();
        }
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.StartAsync(new("cancelled", true, "cancelled"), cancellation.Token));
        Assert.False(Directory.Exists(Path.Combine(root, "runs", "cancelled")));
        Assert.Empty(Directory.GetDirectories(Path.Combine(root, "runs", ".staging")));
        Assert.Equal("unrelated", Assert.Single(await store.ListAsync()).Id);
        Assert.Equal(RunState.Running, (await store.GetAsync("unrelated")).Manifest.State);
    }
}
