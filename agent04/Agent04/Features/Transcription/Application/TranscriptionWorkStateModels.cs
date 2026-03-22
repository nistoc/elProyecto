namespace Agent04.Features.Transcription.Application;

/// <summary>JSON document persisted as <c>transcription_work_state.json</c> next to job artifacts.</summary>
public sealed class TranscriptionWorkStateDocument
{
    public int SchemaVersion { get; set; } = 1;
    public int TotalChunks { get; set; }
    public bool RecoveredFromArtifacts { get; set; }
    public List<TranscriptionWorkStateChunk>? Chunks { get; set; }
}

public sealed class TranscriptionWorkStateChunk
{
    public int Index { get; set; }
    public string State { get; set; } = "";
    public string? StartedAt { get; set; }
    public string? CompletedAt { get; set; }
    public string? ErrorMessage { get; set; }
    public bool IsSubChunk { get; set; }
    public int ParentChunkIndex { get; set; }
    public int SubChunkIndex { get; set; }
}
