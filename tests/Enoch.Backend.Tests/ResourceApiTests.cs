using System.Net.Http.Headers;
using System.Net.Http.Json;
using Enoch.Application;
using Enoch.Protocol;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Enoch.Backend.Tests;

public sealed class ResourceApiTests
{
    [Theory]
    [InlineData("GET", "/api/v1/runs", null)]
    [InlineData("GET", "/api/v1/runs/cancelled", null)]
    [InlineData("GET", "/api/v1/runs/cancelled/plans", null)]
    [InlineData("GET", "/api/v1/runs/cancelled/events", null)]
    [InlineData("GET", "/api/v1/runs/cancelled/evidence", null)]
    [InlineData("GET", "/api/v1/runs/cancelled/artifacts", null)]
    [InlineData("GET", "/api/v1/runs/cancelled/bundle", null)]
    [InlineData("GET", "/api/v1/runs/cancelled/artifacts/unknown", null)]
    [InlineData("GET", "/api/v1/runs/cancelled/evidence/unknown", null)]
    [InlineData("POST", "/api/v1/publish/runs", "{\"title\":\"cancelled\",\"request\":true}")]
    [InlineData("POST", "/api/v1/publish/runs/cancelled/plans", "{\"plan\":true}")]
    [InlineData("POST", "/api/v1/publish/runs/cancelled/events", "{\"eventId\":\"event\",\"kind\":\"progress\"}")]
    [InlineData("POST", "/api/v1/publish/runs/cancelled/evidence", "{\"name\":\"notes\",\"content\":\"body\"}")]
    [InlineData("POST", "/api/v1/publish/runs/cancelled/artifacts", "body")]
    [InlineData("PUT", "/api/v1/publish/runs/cancelled/result", "{\"result\":true}")]
    [InlineData("POST", "/api/v1/publish/runs/cancelled/finish", "{\"outcome\":\"success\"}")]
    [InlineData("POST", "/api/v1/publish/runs/cancelled/wait", null)]
    [InlineData("POST", "/api/v1/publish/runs/cancelled/resume", null)]
    public async Task Actual_route_passes_request_cancellation_to_storage(string method, string path, string? body)
    {
        var store = new CancellationStore();
        using var cancellation = new CancellationTokenSource();
        await using var factory = new CancellationApiFactory(store, cancellation.Token);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");
        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (body != null)
        {
            request.Content = new StringContent(body);
            request.Content.Headers.ContentType = new MediaTypeHeaderValue(path.EndsWith("/artifacts", StringComparison.Ordinal) ? "application/octet-stream" : "application/json");
        }
        var response = client.SendAsync(request, cancellation.Token);
        var captured = await store.Captured.Task.WaitAsync(TimeSpan.FromSeconds(2));
        cancellation.Cancel();
        try
        {
            Assert.True(captured.CanBeCanceled);
            await store.Cancelled.Task.WaitAsync(TimeSpan.FromSeconds(2));
            // Depending on scheduling, TestServer can complete the aborted error
            // response before HttpClient observes its cancellation. Storage must
            // still cancel, and the request must never return a successful result.
            try
            {
                using var aborted = await response.WaitAsync(TimeSpan.FromSeconds(2));
                Assert.False(aborted.IsSuccessStatusCode);
            }
            catch (OperationCanceledException) { }
        }
        finally
        {
            store.Release.TrySetResult();
            try
            { using var ignored = await response; }
            catch (OperationCanceledException) { }
        }
    }

    private sealed class CancellationApiFactory(CancellationStore store, CancellationToken aborted) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("ENOCH_TOKEN", "test-token");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IRunStore>();
                services.AddSingleton<IRunStore>(store);
                services.AddSingleton<IStartupFilter>(new RequestAbortFilter(aborted));
            });
        }
    }

    // TestServer does not model a disconnected socket. Supply its request-abort
    // signal explicitly while retaining the real route binding and filters.
    private sealed class RequestAbortFilter(CancellationToken aborted) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((context, nextMiddleware) =>
            {
                context.RequestAborted = aborted;
                return nextMiddleware(context);
            });
            next(app);
        };
    }

    private sealed class CancellationStore : IRunStore
    {
        public TaskCompletionSource<CancellationToken> Captured { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Cancelled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private async Task<T> Wait<T>(CancellationToken ct)
        {
            Captured.TrySetResult(ct);
            try
            {
                await Release.Task.WaitAsync(ct);
            }
            catch (OperationCanceledException)
            {
                Cancelled.TrySetResult();
                throw;
            }
            throw new InvalidOperationException("The test request must be cancelled.");
        }
        public Task<RunDocument> StartAsync(StartRunRequest request, CancellationToken ct = default) => Wait<RunDocument>(ct);
        public Task<RunDocument> GetAsync(string id, CancellationToken ct = default) => Wait<RunDocument>(ct);
        public Task<IReadOnlyList<RunManifest>> ListAsync(CancellationToken ct = default) => Wait<IReadOnlyList<RunManifest>>(ct);
        public Task<PublicationResponse> PublishPlanAsync(string id, PlanRequest request, CancellationToken ct = default) => Wait<PublicationResponse>(ct);
        public Task<RunEvent> PublishEventAsync(string id, EventRequest request, CancellationToken ct = default) => Wait<RunEvent>(ct);
        public Task<EvidenceInfo> AddEvidenceAsync(string id, EvidenceRequest request, CancellationToken ct = default) => Wait<EvidenceInfo>(ct);
        public Task<EvidenceContent> ReadEvidenceAsync(string id, string evidenceId, CancellationToken ct = default) => Wait<EvidenceContent>(ct);
        public Task<ArtifactInfo> AddArtifactAsync(string id, string name, string mimeType, Stream content, CancellationToken ct = default) => Wait<ArtifactInfo>(ct);
        public Task<ArtifactContent> ReadArtifactAsync(string id, string artifactId, CancellationToken ct = default) => Wait<ArtifactContent>(ct);
        public Task<PublicationResponse> PublishResultAsync(string id, ResultRequest request, CancellationToken ct = default) => Wait<PublicationResponse>(ct);
        public Task<PublicationResponse> FinishAsync(string id, FinishRequest request, CancellationToken ct = default) => Wait<PublicationResponse>(ct);
        public Task<RunDocument> WaitAsync(string id, CancellationToken ct = default) => Wait<RunDocument>(ct);
        public Task<RunDocument> ResumeAsync(string id, CancellationToken ct = default) => Wait<RunDocument>(ct);
    }
}
