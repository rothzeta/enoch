using Enoch.Application;
using Enoch.Protocol;
using Enoch.Storage.FileSystem;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);
var data = builder.Configuration["ENOCH_DATA"] ?? "/data";
var publisherToken = builder.Configuration["ENOCH_TOKEN"];
if (string.IsNullOrWhiteSpace(publisherToken))
{
    throw new InvalidOperationException("ENOCH_TOKEN must contain a publisher token.");
}

builder.Services.AddSingleton<IRunStore>(services => new FileSystemRunStore(data,
    message => Program.LogStorageWarning(services.GetRequiredService<ILogger<FileSystemRunStore>>(), message)));
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)));
var app = builder.Build();
app.UseExceptionHandler(e => e.Run(async context =>
{
    var ex = context.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>()?.Error;
    context.Response.ContentType = "application/problem+json";
    context.Response.StatusCode = ex switch
    {
        EnochException domainError => domainError.Status,
        BadHttpRequestException requestError => requestError.StatusCode,
        _ => 500
    };
    var p = ex switch
    {
        EnochException domainError => new Problem(domainError.Code, domainError.Message, domainError.Details),
        BadHttpRequestException => new Problem("invalid_request", "The HTTP request is invalid."),
        _ => new Problem("internal_error", "An unexpected error occurred.")
    };
    await context.Response.WriteAsJsonAsync(p);
}));

app.UseDefaultFiles();
app.UseStaticFiles();
bool Authorized(HttpContext context)
{
    var supplied = context.Request.Headers.Authorization.ToString();
    var expected = $"Bearer {publisherToken}";
    var suppliedBytes = Encoding.UTF8.GetBytes(supplied);
    var expectedBytes = Encoding.UTF8.GetBytes(expected);
    return suppliedBytes.Length == expectedBytes.Length && CryptographicOperations.FixedTimeEquals(suppliedBytes, expectedBytes);
}
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapGet("/api/v1/runs", async (IRunStore s, CancellationToken ct) => Results.Ok(await s.ListAsync(ct)));
app.MapGet("/api/v1/runs/{id}", async (string id, IRunStore s, CancellationToken ct) => Results.Ok(await s.GetAsync(id, ct)));
app.MapGet("/api/v1/runs/{id}/plans", async (string id, IRunStore s, CancellationToken ct) => (await s.GetAsync(id, ct)).Plans);
app.MapGet("/api/v1/runs/{id}/events", async (string id, IRunStore s, CancellationToken ct) => (await s.GetAsync(id, ct)).Events);
app.MapGet("/api/v1/runs/{id}/evidence", async (string id, IRunStore s, CancellationToken ct) => (await s.GetAsync(id, ct)).Evidence);
app.MapGet("/api/v1/runs/{id}/evidence/{evidenceId}", async (string id, string evidenceId, IRunStore store, HttpContext context, CancellationToken ct) =>
{
    var evidence = await store.ReadEvidenceAsync(id, evidenceId, ct);
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    // The file result owns the stream and serves untrusted evidence as an attachment.
    return Results.File(evidence.Content, evidence.Metadata.MimeType, evidence.Metadata.Name);
});
app.MapGet("/api/v1/runs/{id}/artifacts", async (string id, IRunStore s, CancellationToken ct) => (await s.GetAsync(id, ct)).Artifacts);
app.MapGet("/api/v1/runs/{id}/bundle", async (string id, IRunStore s, CancellationToken ct) => Results.Ok(await s.GetAsync(id, ct)));
var pub = app.MapGroup("/api/v1/publish").AddEndpointFilter(async (ctx, next) =>
{
    if (!Authorized(ctx.HttpContext))
    {
        return Results.Json(new Problem("unauthorized", "Publisher token required."), statusCode: 401);
    }
    return await next(ctx);
});

pub.MapPost("/runs", async (StartRunRequest request, IRunStore store, CancellationToken ct) =>
{
    var run = await store.StartAsync(request, ct);
    return Results.Created($"/api/v1/runs/{run.Manifest.Id}", run);
});
pub.MapPost("/runs/{id}/plans", async (string id, PlanRequest r, IRunStore s, CancellationToken ct) => Results.Ok(await s.PublishPlanAsync(id, r, ct)));
pub.MapPost("/runs/{id}/events", async (string id, EventRequest r, IRunStore s, CancellationToken ct) => Results.Ok(await s.PublishEventAsync(id, r, ct)));
pub.MapPost("/runs/{id}/evidence", async (string id, EvidenceRequest r, IRunStore s, CancellationToken ct) => Results.Ok(await s.AddEvidenceAsync(id, r, ct)));
pub.MapPost("/runs/{id}/artifacts", async (string id, HttpRequest req, IRunStore s, CancellationToken ct) =>
{
    // The store enforces the artifact limit during copy, including chunked bodies.
    // Override Kestrel's smaller default only for this raw streaming endpoint.
    var bodyLimit = req.HttpContext.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpMaxRequestBodySizeFeature>();
    if (bodyLimit is { IsReadOnly: false })
    {
        bodyLimit.MaxRequestBodySize = null;
    }
    return Results.Ok(await s.AddArtifactAsync(id, req.Headers["X-Artifact-Name"].ToString(), req.ContentType ?? "application/octet-stream", req.Body, ct));
});

pub.MapPut("/runs/{id}/result", async (string id, ResultRequest r, IRunStore s, CancellationToken ct) => Results.Ok(await s.PublishResultAsync(id, r, ct)));
pub.MapPost("/runs/{id}/finish", async (string id, FinishRequest r, IRunStore s, CancellationToken ct) => Results.Ok(await s.FinishAsync(id, r, ct)));
pub.MapPost("/runs/{id}/wait", async (string id, IRunStore s, CancellationToken ct) => Results.Ok(await s.WaitAsync(id, ct)));
pub.MapPost("/runs/{id}/resume", async (string id, IRunStore s, CancellationToken ct) => Results.Ok(await s.ResumeAsync(id, ct)));
app.MapGet("/api/v1/runs/{id}/artifacts/{artifactId}", async (string id, string artifactId, IRunStore store, HttpContext context, CancellationToken ct) =>
{
    var artifact = await store.ReadArtifactAsync(id, artifactId, ct);
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    return Results.File(artifact.Content, artifact.Metadata.MimeType, artifact.Metadata.Name);
});

app.MapFallbackToFile("index.html");
app.Run();

public partial class Program
{
    [LoggerMessage(EventId = 1, Level = LogLevel.Warning, Message = "{StorageWarning}")]
    internal static partial void LogStorageWarning(ILogger logger, string storageWarning);
}
