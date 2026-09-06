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
    throw new InvalidOperationException("ENOCH_TOKEN must contain a publisher token.");
builder.Services.AddSingleton<IRunStore>(_ => new FileSystemRunStore(data));
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)));
var app = builder.Build();
app.UseExceptionHandler(e => e.Run(async context => { var ex=context.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>()?.Error; context.Response.ContentType="application/problem+json"; context.Response.StatusCode=ex is EnochException ee?ee.Status:500; var p=ex is EnochException e ? new Problem(e.Code,e.Message,e.Details) : new Problem("internal_error","An unexpected error occurred."); await context.Response.WriteAsJsonAsync(p); }));
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
app.MapGet("/api/v1/runs", async (IRunStore s)=>Results.Ok(await s.ListAsync()));
app.MapGet("/api/v1/runs/{id}", async (string id,IRunStore s)=>Results.Ok(await s.GetAsync(id)));
app.MapGet("/api/v1/runs/{id}/plans", async (string id,IRunStore s)=>(await s.GetAsync(id)).Plans);
app.MapGet("/api/v1/runs/{id}/events", async (string id,IRunStore s)=>(await s.GetAsync(id)).Events);
app.MapGet("/api/v1/runs/{id}/evidence", async (string id,IRunStore s)=>(await s.GetAsync(id)).Evidence);
app.MapGet("/api/v1/runs/{id}/artifacts", async (string id,IRunStore s)=>(await s.GetAsync(id)).Artifacts);
app.MapGet("/api/v1/runs/{id}/bundle", async (string id,IRunStore s)=>Results.Ok(await s.GetAsync(id)));
var pub=app.MapGroup("/api/v1/publish").AddEndpointFilter(async (ctx,next)=>{if(!Authorized(ctx.HttpContext))return Results.Json(new Problem("unauthorized","Publisher token required."),statusCode:401);return await next(ctx);});
pub.MapPost("/runs", async (StartRunRequest r,IRunStore s)=>{var run=await s.StartAsync(r);return Results.Created($"/api/v1/runs/{run.Manifest.Id}",run);});
pub.MapPost("/runs/{id}/plans", async (string id,PlanRequest r,IRunStore s)=>Results.Ok(await s.PublishPlanAsync(id,r)));
pub.MapPost("/runs/{id}/events", async (string id,EventRequest r,IRunStore s)=>Results.Ok(await s.PublishEventAsync(id,r)));
pub.MapPost("/runs/{id}/evidence", async (string id,EvidenceRequest r,IRunStore s)=>Results.Ok(await s.AddEvidenceAsync(id,r)));
pub.MapPost("/runs/{id}/artifacts", async (string id,HttpRequest req,IRunStore s)=>Results.Ok(await s.AddArtifactAsync(id,req.Headers["X-Artifact-Name"].ToString(),req.ContentType??"application/octet-stream",req.Body)));
pub.MapPut("/runs/{id}/result", async (string id,ResultRequest r,IRunStore s)=>Results.Ok(await s.PublishResultAsync(id,r)));
pub.MapPost("/runs/{id}/finish", async (string id,FinishRequest r,IRunStore s)=>Results.Ok(await s.FinishAsync(id,r)));
pub.MapPost("/runs/{id}/wait", async (string id,IRunStore s)=>Results.Ok(await s.WaitAsync(id)));
pub.MapPost("/runs/{id}/resume", async (string id,IRunStore s)=>Results.Ok(await s.ResumeAsync(id)));
app.MapGet("/api/v1/runs/{id}/artifacts/{artifactId}", async (string id,string artifactId,IRunStore s)=>{var r=await s.GetAsync(id);var a=r.Artifacts.FirstOrDefault(x=>x.Id==artifactId)??throw new EnochException("not_found","Artifact was not found.",404);var p=Path.Combine(data,"runs",id,"artifacts",artifactId,"file");return Results.File(p,a.MimeType,a.Name);});
app.MapFallbackToFile("index.html");
app.Run();

public partial class Program { }
