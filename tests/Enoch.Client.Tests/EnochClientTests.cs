using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Enoch.Client;
using Xunit;

namespace Enoch.Client.Tests;

public sealed class EnochClientTests
{
    [Fact]
    public async Task Progress_accepts_the_actual_event_response_without_a_run_id()
    {
        var handler = new RecordingHandler("{\"sequence\":4,\"eventId\":\"step-1\",\"kind\":\"progress\",\"data\":{\"message\":\"Working\"},\"occurredAt\":\"2026-10-03T12:00:00Z\"}");
        using var client = new EnochClient(new EnochClientOptions { BaseAddress = new Uri("https://enoch.test/"), Handler = handler });
        var response = await client.PublishProgressAsync("run", "Working", "step-1");
        Assert.Equal(4, response.Sequence);
        Assert.Null(response.Id);
        Assert.Equal("/api/v1/publish/runs/run/events", handler.Request.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task Result_publication_only_puts_result_and_finish_is_explicit()
    {
        var handler = new RecordingHandler("{\"runId\":\"run\",\"sequence\":3,\"state\":\"running\"}");
        using var client = new EnochClient(new EnochClientOptions { BaseAddress = new Uri("https://enoch.test/"), Handler = handler });
        await client.PublishResultAsync("run", "answer");
        Assert.Equal(HttpMethod.Put, handler.Request.Method);
        Assert.Equal("/api/v1/publish/runs/run/result", handler.Request.RequestUri!.AbsolutePath);
        using (var result = JsonDocument.Parse(handler.RequestBody!))
        {
            Assert.Equal("answer", result.RootElement.GetProperty("result").GetString());
            Assert.Single(result.RootElement.EnumerateObject());
        }
        await client.FinishRunAsync("run", "failed", "Stopped");
        Assert.Equal(HttpMethod.Post, handler.Request.Method);
        Assert.Equal("/api/v1/publish/runs/run/finish", handler.Request.RequestUri!.AbsolutePath);
        using var finish = JsonDocument.Parse(handler.RequestBody!);
        Assert.Equal("Failed", finish.RootElement.GetProperty("outcome").GetString());
        Assert.Equal("Stopped", finish.RootElement.GetProperty("summary").GetString());
    }

    [Fact]
    public async Task Caller_cancellation_remains_cancellation()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        using var client = new EnochClient(new EnochClientOptions { BaseAddress = new Uri("https://enoch.test/"), Handler = new RecordingHandler("{}") });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.ReadRunAsync("run", cancelled.Token));
    }

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
        public string? RequestBody { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            RequestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };
        }
    }
}
