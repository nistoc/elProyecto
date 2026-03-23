using Agent04.Features.Transcription.Domain;

namespace Agent04.Features.Transcription.Application;

/// <summary>
/// Merges sub-chunk transcriptions into a single result: sub-chunks in index order, segments in source order (no global time sort).
/// </summary>
public interface ITranscriptionMerger
{
    /// <summary>
    /// Merge sub-chunk results into one. subResults are (subIndex, TranscriptionResult).
    /// </summary>
    TranscriptionResult MergeTranscriptions(
        IReadOnlyList<(int SubIndex, TranscriptionResult Result)> subResults,
        double parentChunkOffset = 0.0);
}
