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
