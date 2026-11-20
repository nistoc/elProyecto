namespace XtractManager.Features.Jobs.Application;

/// <summary>
/// A line of a model that tells speakers apart (a diarize model), on the timeline of the recording, with the
/// speaker as the model named it. Such a model names the speakers anew in every stretch it is asked about
/// («A», «B» in one chunk, «A», «C» in the next), so a name holds only inside its <see cref="Unit"/>.
/// </summary>
/// <param name="Unit">
/// The stretch the model named the speakers in: a chunk, or one part of a chunk the server refused whole and
/// that was asked in parts. Units are numbered in the order of the audio, one number each, also those that
/// gave no line: two units with numbers one apart follow each other in the audio.
/// </param>
/// <param name="Repeat">
/// The line starts in the stretch the unit shares with the one before it (inside the chunk's emit guard): the
/// previous unit heard the same speech. The transcript leaves such a line out; it is what ties the names of
/// the two units.
/// </param>
public sealed record DiarizedSegment(int Unit, double Start, double End, string Speaker, string Text, bool Repeat);
