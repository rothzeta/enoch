using Enoch.Protocol;
namespace Enoch.Application;
public sealed class RunService(IRunStore store) : IRunStore
{
 public Task<RunDocument> StartAsync(StartRunRequest r, CancellationToken c=default)=>store.StartAsync(r,c);
 public Task<RunDocument> GetAsync(string id,CancellationToken c=default)=>store.GetAsync(id,c);
 public Task<IReadOnlyList<RunManifest>> ListAsync(CancellationToken c=default)=>store.ListAsync(c);
 public Task<PublicationResponse> PublishPlanAsync(string id,PlanRequest r,CancellationToken c=default)=>store.PublishPlanAsync(id,r,c);
 public Task<RunEvent> PublishEventAsync(string id,EventRequest r,CancellationToken c=default)=>store.PublishEventAsync(id,r,c);
 public Task<EvidenceInfo> AddEvidenceAsync(string id,EvidenceRequest r,CancellationToken c=default)=>store.AddEvidenceAsync(id,r,c);
 public Task<ArtifactInfo> AddArtifactAsync(string id,string n,string m,Stream s,CancellationToken c=default)=>store.AddArtifactAsync(id,n,m,s,c);
 public Task<PublicationResponse> PublishResultAsync(string id,ResultRequest r,CancellationToken c=default)=>store.PublishResultAsync(id,r,c);
 public Task<PublicationResponse> FinishAsync(string id,FinishRequest r,CancellationToken c=default)=>store.FinishAsync(id,r,c);
 public Task<RunDocument> WaitAsync(string id,CancellationToken c=default)=>store.WaitAsync(id,c);
 public Task<RunDocument> ResumeAsync(string id,CancellationToken c=default)=>store.ResumeAsync(id,c);
}
