using Enoch.Protocol;

namespace Enoch.Application;

/// <summary>An artifact body and metadata. The caller owns and disposes the stream.</summary>
public sealed record ArtifactContent(ArtifactInfo Metadata, Stream Content);
