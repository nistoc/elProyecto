namespace Agent04.Features.Transcription.Application;

/// <summary>Full job file tree for UI (parity with Xtract <c>JobProjectFiles</c> / GET .../files).</summary>
public sealed record ProjectFilesCatalogResult
{
    public IReadOnlyList<ArtifactFileEntry> Original { get; init; } = Array.Empty<ArtifactFileEntry>();
    public IReadOnlyList<ArtifactFileEntry> Transcripts { get; init; } = Array.Empty<ArtifactFileEntry>();
    public IReadOnlyList<ArtifactFileEntry> Chunks { get; init; } = Array.Empty<ArtifactFileEntry>();
    public IReadOnlyList<ArtifactFileEntry> ChunkJson { get; init; } = Array.Empty<ArtifactFileEntry>();
    public IReadOnlyList<ArtifactFileEntry> Intermediate { get; init; } = Array.Empty<ArtifactFileEntry>();
    public IReadOnlyList<ArtifactFileEntry> Converted { get; init; } = Array.Empty<ArtifactFileEntry>();
    public IReadOnlyList<ArtifactFileEntry> SplitChunks { get; init; } = Array.Empty<ArtifactFileEntry>();
}
