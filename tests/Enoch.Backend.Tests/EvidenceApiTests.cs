using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Enoch.Backend.Tests;

public sealed class EvidenceApiTests
{
    [Fact]
    public async Task Finished_evidence_body_is_an_exact_anonymous_reader_download()
    {
        var data = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        await using var factory = new EvidenceApiFactory(data);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");
        using var started = await client.PostAsJsonAsync("/api/v1/publish/runs", new { title = "evidence", request = true, runId = "evidence-read" });
        Assert.Equal(HttpStatusCode.Created, started.StatusCode);
        const string content = "<script>alert('published')</script>\nλ";
        using var published = await client.PostAsJsonAsync("/api/v1/publish/runs/evidence-read/evidence", new { name = "notes.html", content, mimeType = "text/html" });
        Assert.Equal(HttpStatusCode.OK, published.StatusCode);
        using var metadata = JsonDocument.Parse(await published.Content.ReadAsStringAsync());
        var evidenceId = metadata.RootElement.GetProperty("id").GetString()!;
        using var finished = await client.PostAsJsonAsync("/api/v1/publish/runs/evidence-read/finish", new { outcome = "partial", summary = "Preserved" });
        Assert.Equal(HttpStatusCode.OK, finished.StatusCode);
        client.DefaultRequestHeaders.Authorization = null;

        using var download = await client.GetAsync($"/api/v1/runs/evidence-read/evidence/{evidenceId}");
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal(Encoding.UTF8.GetBytes(content), await download.Content.ReadAsByteArrayAsync());
        Assert.Equal("text/html", download.Content.Headers.ContentType!.MediaType);
        Assert.Equal("attachment", download.Content.Headers.ContentDisposition!.DispositionType);
        Assert.Contains("notes.html", download.Content.Headers.ContentDisposition.ToString(), StringComparison.Ordinal);
        Assert.Equal("nosniff", Assert.Single(download.Headers.GetValues("X-Content-Type-Options")));
        Assert.Equal(Encoding.UTF8.GetByteCount(content), download.Content.Headers.ContentLength);
        using var exclusive = File.Open(Path.Combine(data, "runs", "evidence-read", "evidence", evidenceId, "content"), FileMode.Open, FileAccess.Read, FileShare.None);
    }

    [Theory]
    [InlineData("unknown", "00000000000000000000000000000000", HttpStatusCode.NotFound, "not_found")]
    [InlineData("evidence-read", "00000000000000000000000000000000", HttpStatusCode.NotFound, "not_found")]
    [InlineData("evidence-read", "invalid-id", HttpStatusCode.BadRequest, "invalid_id")]
    public async Task Evidence_lookup_has_documented_not_found_and_invalid_id_responses(string runId, string evidenceId, HttpStatusCode expectedStatus, string expectedCode)
    {
        var data = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        await using var factory = new EvidenceApiFactory(data);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");
        using var started = await client.PostAsJsonAsync("/api/v1/publish/runs", new { title = "evidence", request = true, runId = "evidence-read" });
        Assert.Equal(HttpStatusCode.Created, started.StatusCode);
        client.DefaultRequestHeaders.Authorization = null;
        using var response = await client.GetAsync($"/api/v1/runs/{runId}/evidence/{evidenceId}");
        Assert.Equal(expectedStatus, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(expectedCode, body.RootElement.GetProperty("code").GetString());
    }

    private sealed class EvidenceApiFactory(string data) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder) =>
            builder.UseSetting("ENOCH_DATA", data).UseSetting("ENOCH_TOKEN", "test-token");
    }
}
