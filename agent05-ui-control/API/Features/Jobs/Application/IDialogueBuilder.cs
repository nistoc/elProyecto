namespace XtractManager.Features.Jobs.Application;

/// <summary>One participant track of a dialogue: the job that transcribed it and the name to print.</summary>
/// <param name="JobId">Job whose combined transcript is this participant's track.</param>
/// <param name="Speaker">Name written instead of <c>speaker_N</c>.</param>
/// <param name="File">Bare name of a <c>.md</c> in that job's folder; null = the newest <c>transcript*.md</c> that is not a refiner output.</param>
public sealed record DialogueTrackRequest(string JobId, string Speaker, string? File = null);

public sealed record DialogueCreateRequest(string? Title, IReadOnlyList<DialogueTrackRequest> Tracks);

/// <summary>What one track contributed to a merge.</summary>
public sealed record DialogueTrackResult(string JobId, string Speaker, string File, int Entries, int UntimedEntries);

/// <summary>Outcome of a merge: the dialogue job, the file written and per-track counts.</summary>
public sealed record DialogueBuildResult(
    string JobId,
    string File,
    int Entries,
    IReadOnlyList<DialogueTrackResult> Tracks,
    IReadOnlyList<string> Warnings);

/// <summary>The request cannot be fulfilled; <see cref="NotFound"/> tells a missing job from a bad request.</summary>
public sealed class DialogueRequestException : Exception
{
    public DialogueRequestException(string message, bool notFound = false) : base(message) => NotFound = notFound;

    public bool NotFound { get; }
}

/// <summary>
/// Merges the transcripts of participant tracks (one job per track) into one dialogue that lives in a job
/// folder of its own, so it can be reviewed, edited and refined like any other transcript.
/// </summary>
public interface IDialogueBuilder
{
    /// <summary>Creates a dialogue job from the given tracks. The source jobs are only read.</summary>
    Task<DialogueBuildResult> CreateAsync(DialogueCreateRequest request, CancellationToken ct = default);

    /// <summary>
    /// Merges the tracks of an existing dialogue job again (for example after a track was retranscribed)
    /// and writes a new transcript file next to the earlier ones; nothing is overwritten.
    /// </summary>
    Task<DialogueBuildResult> RebuildAsync(string dialogueJobId, CancellationToken ct = default);
}
