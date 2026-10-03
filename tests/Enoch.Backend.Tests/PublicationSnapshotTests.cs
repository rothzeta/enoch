using System.Text.Json;
using Enoch.Protocol;
using Enoch.Storage.FileSystem;
using Xunit;

namespace Enoch.Backend.Tests;

public sealed class PublicationSnapshotTests
{
    [Fact]
    public async Task Event_response_and_retry_use_the_single_committed_payload_snapshot()
    {
        using var store = new FileSystemRunStore(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));
        await store.StartAsync(new("snapshot", true, "snapshot"));
        var response = await store.PublishEventAsync("snapshot", new("identity", "progress", new ChangingData()));
        var persisted = Assert.Single((await store.GetAsync("snapshot")).Events);
        var retry = await store.PublishEventAsync("snapshot", new("identity", "retry"));
        Assert.Equal(JsonSerializer.Serialize(persisted.Data), JsonSerializer.Serialize(response.Data));
        Assert.Equal(JsonSerializer.Serialize(persisted.Data), JsonSerializer.Serialize(retry.Data));
    }

    private sealed class ChangingData
    {
        private int value;
        public int Value => Interlocked.Increment(ref value);
    }
}
