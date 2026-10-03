using Enoch.Protocol;

namespace Enoch.Application;

/// <summary>Published evidence metadata and bytes. The caller owns and disposes Content.</summary>
public sealed record EvidenceContent(EvidenceInfo Metadata, Stream Content);
