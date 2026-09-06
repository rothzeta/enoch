using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Enoch.Client;

public sealed record StartRunRequest(string Title, object Request);
public sealed record StartRunResponse(string RunId, string? State = null);
public sealed record PlanRequest(object Plan, string? EventId = null);
public sealed record ProgressRequest(string EventId, string Kind, object? Data = null, long? ExpectedSequence = null);
public sealed record EvidenceRequest(string Name, string Content, string? MimeType = "text/plain");
public sealed record ResultRequest(object Result);
public sealed record FinishRequest(string Outcome, string? Summary = null);
public sealed record PublishResponse(string? Id = null, long? Sequence = null, string? State = null);

public sealed class EnochClientOptions
{
    public required Uri BaseAddress { get; init; }
    public string? Token { get; init; }
    public HttpMessageHandler? Handler { get; init; }
}

/// <summary>Small typed adapter for Enoch's publish and read HTTP APIs.</summary>
public sealed class EnochClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly bool _ownsClient;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public EnochClient(EnochClientOptions options)
    {
        _http = options.Handler is null ? new HttpClient() : new HttpClient(options.Handler);
        _ownsClient = true;
        _http.BaseAddress = options.BaseAddress;
        if (!string.IsNullOrWhiteSpace(options.Token))
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", options.Token);
    }

    public async Task<StartRunResponse> StartRunAsync(string title, string request, CancellationToken ct = default)
    {
        var response = await SendAsync(HttpMethod.Post, "api/v1/publish/runs", new StartRunRequest(title, request), ct);
        using var doc = await ReadAsync<JsonDocument>(response, ct);
        var id = doc.RootElement.GetProperty("manifest").GetProperty("id").GetString()!;
        return new StartRunResponse(id, StringValue(doc.RootElement.GetProperty("manifest").GetProperty("state")));
    }

    public Task<PublishResponse> PublishPlanAsync(string runId, string content, string format = "markdown", CancellationToken ct = default) =>
        PublishAsync($"api/v1/publish/runs/{Id(runId)}/plans", new PlanRequest(new { content, format }), ct);

    public Task<PublishResponse> PublishProgressAsync(string runId, string message, string? eventId = null, string kind = "progress", CancellationToken ct = default) =>
        PublishAsync($"api/v1/publish/runs/{Id(runId)}/events", new ProgressRequest(eventId ?? $"cli-{Guid.NewGuid():N}", kind, new { message }), ct);

    public Task<PublishResponse> AddEvidenceAsync(string runId, string name, string content, string contentType = "text/plain", CancellationToken ct = default) =>
        PublishAsync($"api/v1/publish/runs/{Id(runId)}/evidence", new EvidenceRequest(name, content, contentType), ct);

    public async Task<PublishResponse> AddArtifactAsync(string runId, string filePath, string? name = null, string? contentType = null, CancellationToken ct = default)
    {
        await using var stream = File.OpenRead(filePath);
        using var file = new StreamContent(stream);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType ?? "application/octet-stream");
        file.Headers.Add("X-Artifact-Name", name ?? Path.GetFileName(filePath));
        var response = await SendAsync(HttpMethod.Post, $"api/v1/publish/runs/{Id(runId)}/artifacts", file, ct);
        return await ReadPublishAsync(response, ct);
    }

    public Task<PublishResponse> PublishResultAsync(string runId, string content, string? outcome = null, CancellationToken ct = default) =>
        PutAsync($"api/v1/publish/runs/{Id(runId)}/result", new ResultRequest(content), ct);

    public Task<PublishResponse> FinishRunAsync(string runId, string outcome, string? summary = null, CancellationToken ct = default) =>
        PublishAsync($"api/v1/publish/runs/{Id(runId)}/finish", new { outcome = OutcomeName(outcome), summary }, ct);

    public async Task<JsonDocument> ReadRunAsync(string runId, CancellationToken ct = default)
    {
        var response = await SendAsync(HttpMethod.Get, $"api/v1/runs/{Id(runId)}", null, ct);
        return await ReadAsync<JsonDocument>(response, ct);
    }

    private async Task<PublishResponse> PublishAsync(string path, object body, CancellationToken ct)
    {
        var response = await SendAsync(HttpMethod.Post, path, body, ct);
        return await ReadPublishAsync(response, ct);
    }
    private async Task<PublishResponse> PutAsync(string path, object body, CancellationToken ct)
    {
        var response = await SendAsync(HttpMethod.Put, path, body, ct);
        return await ReadPublishAsync(response, ct);
    }
    private static async Task<PublishResponse> ReadPublishAsync(HttpResponseMessage response, CancellationToken ct)
    {
        using var doc = await ReadAsync<JsonDocument>(response, ct);
        var root = doc.RootElement;
        var source = root.TryGetProperty("manifest", out var manifest) ? manifest : root;
        string? id = source.TryGetProperty("id", out var idValue) ? idValue.GetString()
            : root.TryGetProperty("runId", out var runIdValue) ? runIdValue.GetString() : null;
        string? state = source.TryGetProperty("state", out var stateValue) ? StringValue(stateValue) : null;
        long? sequence = source.TryGetProperty("sequence", out var seqValue) && seqValue.TryGetInt64(out var seq) ? seq : null;
        return new PublishResponse(id, sequence, state);
    }
    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, object? body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body is HttpContent content) request.Content = content;
        else if (body is not null) request.Content = JsonContent.Create(body, options: JsonOptions);
        var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode)
        {
            var detail = await response.Content.ReadAsStringAsync(ct);
            throw new EnochApiException(response.StatusCode, detail);
        }
        return response;
    }
    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, CancellationToken ct)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        var value = await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, ct);
        return value ?? throw new EnochApiException(HttpStatusCode.UnprocessableEntity, "Enoch returned an empty response.");
    }
    private static string Id(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Any(c => c is '/' or '\\' or '?' or '#')) throw new ArgumentException("Invalid run id.", nameof(value));
        return Uri.EscapeDataString(value);
    }
    private static string OutcomeName(string value) => value.ToLowerInvariant() switch
    {
        "success" => "Success", "partial" => "Partial", "failed" => "Failed", "cancelled" => "Cancelled", "expired" => "Expired",
        _ => throw new ArgumentException("Outcome must be success, partial, failed, cancelled, or expired.", nameof(value))
    };
    private static string StringValue(JsonElement value) => value.ValueKind == JsonValueKind.Number
        ? value.GetInt32() switch { 0 => "Queued", 1 => "Running", 2 => "Waiting", 3 => "Finished", _ => value.GetRawText() }
        : value.GetString() ?? value.GetRawText();
    public void Dispose() { if (_ownsClient) _http.Dispose(); }
}

public sealed class EnochApiException(HttpStatusCode statusCode, string detail) : Exception($"Enoch API returned {(int)statusCode} ({statusCode}): {detail}")
{
    public HttpStatusCode StatusCode { get; } = statusCode;
    public string Detail { get; } = detail;
}
