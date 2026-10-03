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
            request = new
            {
                prompt = "hello"
            },
            runId = "response-regression"
        });
        Assert.Equal(HttpStatusCode.Created, started.StatusCode);

        await AssertPublicationResponse(
            await client.PostAsJsonAsync("/api/v1/publish/runs/response-regression/plans", new
            {
                plan = new { steps = new[] { "work" } }
            }),
            1,
            "running");
        await AssertPublicationResponse(
            await client.PutAsJsonAsync("/api/v1/publish/runs/response-regression/result", new
            {
                result = new
                {
                    answer = "ok"
                }
            }),
            1,
            "running");
        await AssertPublicationResponse(
            await client.PostAsJsonAsync("/api/v1/publish/runs/response-regression/finish", new
            {
                outcome = "partial"
            }),
            1,
            "finished");
        var bundle = await client.GetAsync("/api/v1/runs/response-regression/bundle");
        Assert.Equal(HttpStatusCode.OK, bundle.StatusCode);
        using var published = JsonDocument.Parse(await bundle.Content.ReadAsStringAsync());
        Assert.Equal("partial", published.RootElement.GetProperty("manifest").GetProperty("outcome").GetString());
        Assert.Single(published.RootElement.GetProperty("plans").EnumerateArray());
    }

    [Theory]
    [InlineData("request", "null")]
    [InlineData("request", "missing")]
    [InlineData("plan", "null")]
    [InlineData("plan", "missing")]
    [InlineData("result", "null")]
    [InlineData("result", "missing")]
    public async Task Invalid_required_payload_is_rejected_without_changing_storage(string payload, string value)
    {
        var data = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        await using var factory = new EnochApiFactory(data);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");
        var started = await client.PostAsJsonAsync("/api/v1/publish/runs", new { title = "valid", request = new { input = 1 }, runId = "valid-run" });
        Assert.Equal(HttpStatusCode.Created, started.StatusCode);
        var before = Directory.GetFiles(data, "*", SearchOption.AllDirectories).Where(path => Path.GetFileName(path) != ".enoch-store.lock").ToDictionary(path => path, File.ReadAllText);
        var json = value == "missing" ? "{}" : $"{{\"{payload}\":null}}";
        if (payload == "request")
        {
            json = value == "missing" ? "{\"title\":\"invalid\",\"runId\":\"invalid-run\"}" : "{\"title\":\"invalid\",\"runId\":\"invalid-run\",\"request\":null}";
        }
        using var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
        var response = payload switch
        {
            "request" => await client.PostAsync("/api/v1/publish/runs", content),
            "plan" => await client.PostAsync("/api/v1/publish/runs/valid-run/plans", content),
            _ => await client.PutAsync("/api/v1/publish/runs/valid-run/result", content)
        };
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal($"invalid_{payload}", problem.RootElement.GetProperty("code").GetString());
        Assert.Equal(before.Keys.Order(), Directory.GetFiles(data, "*", SearchOption.AllDirectories).Where(path => Path.GetFileName(path) != ".enoch-store.lock").Order());
        foreach (var file in before)
        {
            Assert.Equal(file.Value, await File.ReadAllTextAsync(file.Key));
        }
        var bundle = await client.GetAsync("/api/v1/runs/valid-run/bundle");
        Assert.Equal(HttpStatusCode.OK, bundle.StatusCode);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"outcome\":null}")]
    [InlineData("{\"outcome\":999}")]
    [InlineData("{\"outcome\":\"unknown\"}")]
    public async Task Missing_or_invalid_finish_outcome_leaves_run_active(string json)
    {
        var data = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        await using var factory = new EnochApiFactory(data);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");
        await client.PostAsJsonAsync("/api/v1/publish/runs", new { title = "valid", request = 42, runId = "valid-run" });
        var before = Directory.GetFiles(data, "*", SearchOption.AllDirectories).Where(path => Path.GetFileName(path) != ".enoch-store.lock").ToDictionary(path => path, File.ReadAllText);
        using var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
        var response = await client.PostAsync("/api/v1/publish/runs/valid-run/finish", content);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        foreach (var file in before)
        {
            Assert.Equal(file.Value, await File.ReadAllTextAsync(file.Key));
        }
        using var bundle = JsonDocument.Parse(await client.GetStringAsync("/api/v1/runs/valid-run/bundle"));
        Assert.Equal("running", bundle.RootElement.GetProperty("manifest").GetProperty("state").GetString());
        Assert.Equal(JsonValueKind.Null, bundle.RootElement.GetProperty("manifest").GetProperty("outcome").ValueKind);
    }

    [Theory]
    [InlineData("plans", "{\"plan\":{}}")]
    [InlineData("events", "{\"eventId\":\"event\",\"kind\":\"progress\"}")]
    [InlineData("evidence", "{\"name\":\"notes\",\"content\":\"text\"}")]
    [InlineData("artifacts", "payload")]
    [InlineData("result", "{\"result\":{}}")]
    [InlineData("finish", "{\"outcome\":\"success\"}")]
    [InlineData("wait", "{}")]
    [InlineData("resume", "{}")]
    public async Task Unknown_run_mutations_return_not_found_without_creating_storage(string operation, string json)
    {
        var data = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        await using var factory = new EnochApiFactory(data);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/runs")).StatusCode);
        var before = Directory.GetFiles(data, "*", SearchOption.AllDirectories).Where(path => Path.GetFileName(path) != ".enoch-store.lock").ToDictionary(path => path, File.ReadAllText);
        using var content = new StringContent(json, System.Text.Encoding.UTF8, operation == "artifacts" ? "text/plain" : "application/json");
        var endpoint = $"/api/v1/publish/runs/unknown-run/{operation}";
        var response = operation == "result" ? await client.PutAsync(endpoint, content) : await client.PostAsync(endpoint, content);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("not_found", problem.RootElement.GetProperty("code").GetString());
        Assert.Equal(before.Keys.Order(), Directory.GetFiles(data, "*", SearchOption.AllDirectories).Where(path => Path.GetFileName(path) != ".enoch-store.lock").Order());
        foreach (var file in before)
        {
            Assert.Equal(file.Value, await File.ReadAllTextAsync(file.Key));
        }
        Assert.False(Directory.Exists(Path.Combine(data, "runs", "unknown-run")));
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
