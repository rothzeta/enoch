using Enoch.Protocol;

namespace Enoch.Application;
public interface IRunStore
{
    Task<RunDocument> StartAsync(StartRunRequest request, CancellationToken ct = default);
    Task<RunDocument> GetAsync(string id, CancellationToken ct = default);
    Task<IReadOnlyList<RunManifest>> ListAsync(CancellationToken ct = default);
    Task<PublicationResponse> PublishPlanAsync(string id, PlanRequest request, CancellationToken ct = default);
    Task<RunEvent> PublishEventAsync(string id, EventRequest request, CancellationToken ct = default);
    Task<EvidenceInfo> AddEvidenceAsync(string id, EvidenceRequest request, CancellationToken ct = default);
    Task<ArtifactInfo> AddArtifactAsync(string id, string name, string mimeType, Stream content, CancellationToken ct = default);
    Task<PublicationResponse> PublishResultAsync(string id, ResultRequest request, CancellationToken ct = default);
    Task<PublicationResponse> FinishAsync(string id, FinishRequest request, CancellationToken ct = default);
    Task<RunDocument> WaitAsync(string id, CancellationToken ct = default);
    Task<RunDocument> ResumeAsync(string id, CancellationToken ct = default);
}
