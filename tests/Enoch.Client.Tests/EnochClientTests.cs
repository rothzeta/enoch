using System.Net;
using System.Net.Http.Headers;
using Enoch.Client;
using Xunit;

namespace Enoch.Client.Tests;

public sealed class EnochClientTests
{
    [Fact]
    public async Task Start_extracts_manifest_id_and_uses_bearer_token()
    {
        var handler = new RecordingHandler("{\"manifest\":{\"id\":\"run-1\",\"state\":\"Running\"}}");
        using var client = new EnochClient(new EnochClientOptions { BaseAddress = new Uri("https://enoch.test/"), Token = "secret", Handler = handler });
        var run = await client.StartRunAsync("title", "request");
        Assert.Equal("run-1", run.RunId);
        Assert.Equal("/api/v1/publish/runs", handler.Request.RequestUri!.AbsolutePath);
        Assert.Equal("Bearer secret", handler.Request.Headers.Authorization!.ToString());
    }

    [Fact]
    public async Task Invalid_run_id_is_rejected_before_request()
    {
        using var client = new EnochClient(new EnochClientOptions { BaseAddress = new Uri("https://enoch.test/") });
        await Assert.ThrowsAsync<ArgumentException>(() => client.PublishProgressAsync("../escape", "nope"));
    }

    [Theory]
    [InlineData("plan")]
    [InlineData("result")]
    [InlineData("finish")]
    public async Task Mutations_read_stable_publication_response(string mutation)
    {
        var handler = new RecordingHandler("{\"runId\":\"run-1\",\"sequence\":7,\"state\":\"finished\"}");
        using var client = new EnochClient(new EnochClientOptions { BaseAddress = new Uri("https://enoch.test/"), Handler = handler });

        var response = mutation switch
        {
            "plan" => await client.PublishPlanAsync("run-1", "plan"),
            "result" => await client.PublishResultAsync("run-1", "result"),
            _ => await client.FinishRunAsync("run-1", "partial")
        };

        Assert.Equal("run-1", response.Id);
        Assert.Equal(7, response.Sequence);
        Assert.Equal("finished", response.State);
    }

    [Fact]
    public async Task Evidence_and_artifact_ids_remain_compatible()
    {
        var evidenceHandler = new RecordingHandler("{\"id\":\"evidence-1\"}");
        using var evidenceClient = new EnochClient(new EnochClientOptions { BaseAddress = new Uri("https://enoch.test/"), Handler = evidenceHandler });
        Assert.Equal("evidence-1", (await evidenceClient.AddEvidenceAsync("run-1", "notes", "done")).Id);

        var path = Path.GetTempFileName();
        try
        {
            var artifactHandler = new RecordingHandler("{\"id\":\"artifact-1\"}");
            using var artifactClient = new EnochClient(new EnochClientOptions { BaseAddress = new Uri("https://enoch.test/"), Handler = artifactHandler });
            Assert.Equal("artifact-1", (await artifactClient.AddArtifactAsync("run-1", path)).Id);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private sealed class RecordingHandler(string body) : HttpMessageHandler
    {
        public HttpRequestMessage Request { get; private set; } = null!;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") });
        }
    }
}
