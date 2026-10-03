using Enoch.Protocol;
using Enoch.Storage.FileSystem;
using Xunit;

namespace Enoch.Backend.Tests;

public sealed class EmptyBundleRestoreTests
{
    [Fact]
    public async Task Restored_empty_bundle_without_optional_directories_remains_readable_and_publishable()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        using (var initial = new FileSystemRunStore(root))
        {
            await initial.StartAsync(new("empty", true, "empty"));
        }
        var directory = Path.Combine(root, "runs", "empty");
        Directory.Delete(Path.Combine(directory, "plans"));
        Directory.Delete(Path.Combine(directory, "evidence"));
        Directory.Delete(Path.Combine(directory, "artifacts"));
        using var restored = new FileSystemRunStore(root);
        var run = await restored.GetAsync("empty");
        Assert.Equal(RunState.Running, run.Manifest.State);
        Assert.Empty(run.Plans);
        Assert.Empty(run.Events);
        Assert.Empty(run.Evidence);
        Assert.Empty(run.Artifacts);
        Assert.Equal(1, (await restored.PublishPlanAsync("empty", new(true, "plan"))).Sequence);
        Assert.Equal(2, (await restored.PublishEventAsync("empty", new("event", "progress"))).Sequence);
        Assert.Equal(2, (await restored.GetAsync("empty")).Manifest.Sequence);
    }
}
