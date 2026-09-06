using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Enoch.Backend.Tests;

public sealed class PublicationApiTests
{
    [Fact]
    public async Task Plan_result_and_finish_return_stable_publication_responses()
    {
        var data = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        await using var factory = new EnochApiFactory(data);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");

        var started = await client.PostAsJsonAsync("/api/v1/publish/runs", new
        {
            title = "response regression",
            request = new { prompt = "hello" },
            runId = "response-regression"
        });
        Assert.Equal(HttpStatusCode.Created, started.StatusCode);

        await AssertPublicationResponse(
            await client.PostAsJsonAsync("/api/v1/publish/runs/response-regression/plans", new { plan = (object?)null }),
            1,
            "running");
        await AssertPublicationResponse(
            await client.PutAsJsonAsync("/api/v1/publish/runs/response-regression/result", new { result = new { answer = "ok" } }),
            1,
            "running");
        await AssertPublicationResponse(
            await client.PostAsJsonAsync("/api/v1/publish/runs/response-regression/finish", new { outcome = "partial" }),
            1,
            "finished");
    }

    private static async Task AssertPublicationResponse(HttpResponseMessage response, long sequence, string state)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("response-regression", body.RootElement.GetProperty("runId").GetString());
        Assert.Equal(sequence, body.RootElement.GetProperty("sequence").GetInt64());
        Assert.Equal(state, body.RootElement.GetProperty("state").GetString());
        Assert.Equal(3, body.RootElement.EnumerateObject().Count());
    }

    private sealed class EnochApiFactory(string data) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder) =>
            builder.UseSetting("ENOCH_DATA", data).UseSetting("ENOCH_TOKEN", "test-token");
    }
}
