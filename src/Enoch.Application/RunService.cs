using Enoch.Protocol;
namespace Enoch.Application;
public sealed class RunService(IRunStore store) : IRunStore
{
    public Task<RunDocument> StartAsync(StartRunRequest request, CancellationToken ct = default) => store.StartAsync(request, ct);
    public Task<RunDocument> GetAsync(string id, CancellationToken ct = default) => store.GetAsync(id, ct);
    public Task<IReadOnlyList<RunManifest>> ListAsync(CancellationToken ct = default) => store.ListAsync(ct);
    public Task<PublicationResponse> PublishPlanAsync(string id, PlanRequest request, CancellationToken ct = default) => store.PublishPlanAsync(id, request, ct);
    public Task<RunEvent> PublishEventAsync(string id, EventRequest request, CancellationToken ct = default) => store.PublishEventAsync(id, request, ct);
    public Task<EvidenceInfo> AddEvidenceAsync(string id, EvidenceRequest request, CancellationToken ct = default) => store.AddEvidenceAsync(id, request, ct);
    public Task<EvidenceContent> ReadEvidenceAsync(string id, string evidenceId, CancellationToken ct = default) => store.ReadEvidenceAsync(id, evidenceId, ct);
    public Task<ArtifactInfo> AddArtifactAsync(string id, string name, string mimeType, Stream content, CancellationToken ct = default) => store.AddArtifactAsync(id, name, mimeType, content, ct);
    public Task<ArtifactContent> ReadArtifactAsync(string id, string artifactId, CancellationToken ct = default) => store.ReadArtifactAsync(id, artifactId, ct);
    public Task<PublicationResponse> PublishResultAsync(string id, ResultRequest request, CancellationToken ct = default) => store.PublishResultAsync(id, request, ct);
    public Task<PublicationResponse> FinishAsync(string id, FinishRequest request, CancellationToken ct = default) => store.FinishAsync(id, request, ct);
    public Task<RunDocument> WaitAsync(string id, CancellationToken ct = default) => store.WaitAsync(id, ct);
    public Task<RunDocument> ResumeAsync(string id, CancellationToken ct = default) => store.ResumeAsync(id, ct);
}
