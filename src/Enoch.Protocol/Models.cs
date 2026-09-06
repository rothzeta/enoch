namespace Enoch.Protocol;

public enum RunState { Queued, Running, Waiting, Finished }
public enum RunOutcome { Success, Partial, Failed, Cancelled, Expired }

public sealed record StartRunRequest(string Title, object Request, string? RunId = null);
public sealed record PlanRequest(object Plan, string? EventId = null);
public sealed record EventRequest(string EventId, string Kind, object? Data = null, long? ExpectedSequence = null, DateTimeOffset? OccurredAt = null);
public sealed record ResultRequest(object Result);
public sealed record FinishRequest(RunOutcome Outcome, string? Summary = null);
public sealed record EvidenceRequest(string Name, string Content, string? MimeType = "text/plain");
public sealed record PublicationResponse(string RunId, long Sequence, RunState State);
public sealed record RunManifest(string Id, string Title, RunState State, RunOutcome? Outcome, long Sequence, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, DateTimeOffset? FinishedAt, string? Summary = null);
public sealed record RunEvent(long Sequence, string EventId, string Kind, object? Data, DateTimeOffset OccurredAt);
public sealed record ArtifactInfo(string Id, string Name, string MimeType, long Length, string Sha256, DateTimeOffset CreatedAt);
public sealed record EvidenceInfo(string Id, string Name, string MimeType, long Length, string Sha256, DateTimeOffset CreatedAt);
public sealed record RunDocument(RunManifest Manifest, object Request, IReadOnlyList<object> Plans, IReadOnlyList<RunEvent> Events, object? Result, IReadOnlyList<EvidenceInfo> Evidence, IReadOnlyList<ArtifactInfo> Artifacts);
