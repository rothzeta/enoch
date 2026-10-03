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
    /// <summary>Deadline for the complete HTTP request, including its response body.</summary>
    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(100);
}

/// <summary>Small typed adapter for Enoch's publish and read HTTP APIs.</summary>
public sealed class EnochClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly bool _ownsClient;
    private readonly TimeSpan _requestTimeout;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public EnochClient(EnochClientOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(options.BaseAddress);
        if (options.RequestTimeout <= TimeSpan.Zero || options.RequestTimeout.TotalMilliseconds > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "RequestTimeout must be positive and finite.");
        }
        _requestTimeout = options.RequestTimeout;
        _http = options.Handler is null ? new HttpClient() : new HttpClient(options.Handler);
        _ownsClient = true;
        _http.BaseAddress = options.BaseAddress;
        _http.Timeout = _requestTimeout;
        if (!string.IsNullOrWhiteSpace(options.Token))
        {
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", options.Token);
        }
    }

    public async Task<StartRunResponse> StartRunAsync(string title, string request, CancellationToken ct = default)
    {
        using var doc = await RequestAsync(HttpMethod.Post, "api/v1/publish/runs", new StartRunRequest(title, request), ReadAsync<JsonDocument>, ct);
        var manifest = RequireManifest(doc.RootElement);
        return new StartRunResponse(RequiredString(manifest, "id"), manifest.TryGetProperty("state", out var state) ? StringValue(state) : null);
    }

    public Task<PublishResponse> PublishPlanAsync(string runId, string content, string format = "markdown", CancellationToken ct = default) =>
        PublishAsync($"api/v1/publish/runs/{Id(runId)}/plans", new PlanRequest(new
        {
            content,
            format
        }), ct);

    public Task<PublishResponse> PublishProgressAsync(string runId, string message, string? eventId = null, string kind = "progress", CancellationToken ct = default) =>
        PublishAsync($"api/v1/publish/runs/{Id(runId)}/events", new ProgressRequest(eventId ?? $"cli-{Guid.NewGuid():N}", kind, new
        {
            message
        }), ct);

    public Task<PublishResponse> AddEvidenceAsync(string runId, string name, string content, string contentType = "text/plain", CancellationToken ct = default) =>
        PublishAsync($"api/v1/publish/runs/{Id(runId)}/evidence", new EvidenceRequest(name, content, contentType), ct);

    public async Task<PublishResponse> AddArtifactAsync(string runId, string filePath, string? name = null, string? contentType = null, CancellationToken ct = default)
    {
        await using var stream = File.OpenRead(filePath);
        using var file = new StreamContent(stream);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType ?? "application/octet-stream");
        file.Headers.Add("X-Artifact-Name", name ?? Path.GetFileName(filePath));
        return await RequestAsync(HttpMethod.Post, $"api/v1/publish/runs/{Id(runId)}/artifacts", file, ReadPublishAsync, ct);
    }

    /// <summary>Publishes result content. Record a terminal outcome separately with FinishRunAsync.</summary>
    public Task<PublishResponse> PublishResultAsync(string runId, string content, CancellationToken ct = default) =>
        PutAsync($"api/v1/publish/runs/{Id(runId)}/result", new ResultRequest(content), ct);

    public Task<PublishResponse> FinishRunAsync(string runId, string outcome, string? summary = null, CancellationToken ct = default) =>
        PublishAsync($"api/v1/publish/runs/{Id(runId)}/finish", new
        {
            outcome = OutcomeName(outcome),
            summary
        }, ct);

    public async Task<JsonDocument> ReadRunAsync(string runId, CancellationToken ct = default)
    {
        var document = await RequestAsync(HttpMethod.Get, $"api/v1/runs/{Id(runId)}", null, ReadAsync<JsonDocument>, ct);
        try
        {
            RequiredString(RequireManifest(document.RootElement), "id");
            return document;
        }
        catch
        {
            document.Dispose();
            throw;
        }
    }

    private Task<PublishResponse> PublishAsync(string path, object body, CancellationToken ct) =>
        RequestAsync(HttpMethod.Post, path, body, ReadPublishAsync, ct);
    private Task<PublishResponse> PutAsync(string path, object body, CancellationToken ct) =>
        RequestAsync(HttpMethod.Put, path, body, ReadPublishAsync, ct);
    private async Task<T> RequestAsync<T>(HttpMethod method, string path, object? body, Func<HttpResponseMessage, CancellationToken, Task<T>> read, CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(_requestTimeout);
        // Headers and body parsing share the same deadline; ResponseHeadersRead alone does not bound the body.
        using var response = await SendAsync(method, path, body, deadline.Token);
        return await read(response, deadline.Token);
    }
    private static async Task<PublishResponse> ReadPublishAsync(HttpResponseMessage response, CancellationToken ct)
    {
        using var doc = await ReadAsync<JsonDocument>(response, ct);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw InvalidResponse();
        }
        var source = root.TryGetProperty("manifest", out var manifest) ? manifest : root;
        if (source.ValueKind != JsonValueKind.Object)
        {
            throw InvalidResponse();
        }
        string? id = source.TryGetProperty("id", out _) ? RequiredString(source, "id")
            : root.TryGetProperty("runId", out _) ? RequiredString(root, "runId") : null;
        string? state = source.TryGetProperty("state", out var stateValue) ? StringValue(stateValue) : null;
        long? sequence = null;
        if (source.TryGetProperty("sequence", out var seqValue))
        {
            if (seqValue.ValueKind != JsonValueKind.Number || !seqValue.TryGetInt64(out var seq) || seq < 0)
            {
                throw InvalidResponse();
            }
            sequence = seq;
        }
        if (id is null && !IsEventResponse(root, sequence))
        {
            throw InvalidResponse();
        }
        return new PublishResponse(id, sequence, state);
    }
    private static bool IsEventResponse(JsonElement root, long? sequence) =>
        sequence > 0 && !root.TryGetProperty("manifest", out _) &&
        root.TryGetProperty("eventId", out var eventId) && eventId.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(eventId.GetString()) &&
        root.TryGetProperty("kind", out var kind) && kind.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(kind.GetString()) &&
        root.TryGetProperty("occurredAt", out var occurredAt) && occurredAt.ValueKind == JsonValueKind.String && occurredAt.TryGetDateTimeOffset(out _);
    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, object? body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body is HttpContent content)
        {
            request.Content = content;
        }
        else if (body is not null)
        {
            request.Content = JsonContent.Create(body, options: JsonOptions);
        }

        var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode)
        {
            using (response)
            {
                var detail = await response.Content.ReadAsStringAsync(ct);
                throw new EnochApiException(response.StatusCode, detail);
            }
        }
        return response;
    }
    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            var value = await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, ct);
            return value ?? throw InvalidResponse();
        }
        catch (JsonException)
        {
            throw InvalidResponse();
        }
    }
    private static EnochApiException InvalidResponse() => new(HttpStatusCode.BadGateway, "Enoch returned an invalid response. Check ENOCH_URL and the server response.");
    private static JsonElement RequireManifest(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("manifest", out var manifest) || manifest.ValueKind != JsonValueKind.Object)
        {
            throw InvalidResponse();
        }
        return manifest;
    }
    private static string RequiredString(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()))
        {
            throw InvalidResponse();
        }
        return value.GetString()!;
    }
    private static string Id(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Any(c => c is '/' or '\\' or '?' or '#'))
        {
            throw new ArgumentException("Invalid run id.", nameof(value));
        }

        return Uri.EscapeDataString(value);
    }
    private static string OutcomeName(string value) => value.ToLowerInvariant() switch
    {
        "success" => "Success",
        "partial" => "Partial",
        "failed" => "Failed",
        "cancelled" => "Cancelled",
        "expired" => "Expired",
        _ => throw new ArgumentException("Outcome must be success, partial, failed, cancelled, or expired.", nameof(value))
    };
    private static string StringValue(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.String)
        {
            return value.GetString()!;
        }
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var state))
        {
            return state switch { 0 => "Queued", 1 => "Running", 2 => "Waiting", 3 => "Finished", _ => value.GetRawText() };
        }
        throw InvalidResponse();
    }
    public void Dispose()
    {
        if (_ownsClient)
        {
            _http.Dispose();
        }
    }
}

public sealed class EnochApiException(HttpStatusCode statusCode, string detail) : Exception($"Enoch API returned {(int)statusCode} ({statusCode}): {detail}")
{
    public HttpStatusCode StatusCode { get; } = statusCode;
    public string Detail { get; } = detail;
}
