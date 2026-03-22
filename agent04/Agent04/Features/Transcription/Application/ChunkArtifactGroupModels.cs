namespace Agent04.Features.Transcription.Application;

/// <summary>One scanned file under a job artifact root (Stats / grouping); mirrors agent05 JobProjectFile shape.</summary>
public sealed record ArtifactFileEntry
{
    public string Name { get; init; } = "";
    public string RelativePath { get; init; } = "";
    public long SizeBytes { get; init; }
    public string Kind { get; init; } = "other";
    public int? LineCount { get; init; }
    public double? DurationSeconds { get; init; }
    public int? Index { get; init; }
    public int? ParentIndex { get; init; }
    public int? SubIndex { get; init; }
    public bool? HasTranscript { get; init; }
    public bool? IsTranscript { get; init; }
}

public sealed record SubChunkArtifactGroupResult
{
    public int? SubIndex { get; init; }
    public string DisplayStem { get; init; } = "";
    public IReadOnlyList<ArtifactFileEntry> AudioFiles { get; init; } = Array.Empty<ArtifactFileEntry>();
    public IReadOnlyList<ArtifactFileEntry> JsonFiles { get; init; } = Array.Empty<ArtifactFileEntry>();
}

public sealed record ChunkArtifactGroupResult
{
    public int Index { get; init; }
    public string DisplayStem { get; init; } = "";
    public IReadOnlyList<ArtifactFileEntry> AudioFiles { get; init; } = Array.Empty<ArtifactFileEntry>();
    public IReadOnlyList<ArtifactFileEntry> JsonFiles { get; init; } = Array.Empty<ArtifactFileEntry>();
    public IReadOnlyList<SubChunkArtifactGroupResult> SubChunks { get; init; } = Array.Empty<SubChunkArtifactGroupResult>();
    public IReadOnlyList<ArtifactFileEntry> MergedSplitFiles { get; init; } = Array.Empty<ArtifactFileEntry>();
}
