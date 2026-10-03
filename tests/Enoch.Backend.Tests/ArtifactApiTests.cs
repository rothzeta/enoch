using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Enoch.Backend.Tests;

public sealed class ArtifactApiTests
{
    [Fact]
    public async Task Finished_artifact_has_exact_bytes_attachment_headers_and_disposed_stream()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        await using var factory = new ArtifactApiFactory(root);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");
        using var started = await client.PostAsJsonAsync("/api/v1/publish/runs", new { title = "artifact", request = true, runId = "artifact-read" });
        Assert.Equal(HttpStatusCode.Created, started.StatusCode);
        byte[] bytes = [0, 1, 127, 255];
        using var body = new ByteArrayContent(bytes);
        body.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        using var upload = new HttpRequestMessage(HttpMethod.Post, "/api/v1/publish/runs/artifact-read/artifacts") { Content = body };
        upload.Headers.Add("X-Artifact-Name", "binary.bin");
        using var published = await client.SendAsync(upload);
        Assert.Equal(HttpStatusCode.OK, published.StatusCode);
        using var metadata = JsonDocument.Parse(await published.Content.ReadAsStringAsync());
        var artifactId = metadata.RootElement.GetProperty("id").GetString()!;
        using var finished = await client.PostAsJsonAsync("/api/v1/publish/runs/artifact-read/finish", new { outcome = "success" });
        Assert.Equal(HttpStatusCode.OK, finished.StatusCode);
        client.DefaultRequestHeaders.Authorization = null;
        using var download = await client.GetAsync($"/api/v1/runs/artifact-read/artifacts/{artifactId}");
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal(bytes, await download.Content.ReadAsByteArrayAsync());
        Assert.Equal(bytes.Length, download.Content.Headers.ContentLength);
        Assert.Equal("attachment", download.Content.Headers.ContentDisposition!.DispositionType);
        Assert.Contains("binary.bin", download.Content.Headers.ContentDisposition.ToString(), StringComparison.Ordinal);
        Assert.Equal("nosniff", Assert.Single(download.Headers.GetValues("X-Content-Type-Options")));
        using var exclusive = File.Open(Path.Combine(root, "runs", "artifact-read", "artifacts", artifactId, "file"), FileMode.Open, FileAccess.Read, FileShare.None);
    }

    [Theory]
    [InlineData("unknown", "00000000000000000000000000000000", HttpStatusCode.NotFound, "not_found")]
    [InlineData("artifact-read", "00000000000000000000000000000000", HttpStatusCode.NotFound, "not_found")]
    [InlineData("artifact-read", "invalid-id", HttpStatusCode.BadRequest, "invalid_id")]
    public async Task Unknown_or_invalid_artifact_has_domain_response(string runId, string artifactId, HttpStatusCode status, string code)
    {
        await using var factory = new ArtifactApiFactory(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")));
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");
        using var started = await client.PostAsJsonAsync("/api/v1/publish/runs", new { title = "artifact", request = true, runId = "artifact-read" });
        Assert.Equal(HttpStatusCode.Created, started.StatusCode);
        client.DefaultRequestHeaders.Authorization = null;
        using var response = await client.GetAsync($"/api/v1/runs/{runId}/artifacts/{artifactId}");
        Assert.Equal(status, response.StatusCode);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(code, problem.RootElement.GetProperty("code").GetString());
    }

    private sealed class ArtifactApiFactory(string root) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder) => builder.UseSetting("ENOCH_DATA", root).UseSetting("ENOCH_TOKEN", "test-token");
    }
}
