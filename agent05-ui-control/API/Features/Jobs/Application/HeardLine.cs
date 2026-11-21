namespace XtractManager.Features.Jobs.Application;

/// <summary>
/// A line of a job of the plain-text model (<c>gpt-transcribe</c>) on the timeline of the recording, with what
/// the transcriber's other hearings of the same speech had for it (block <c>speech_pieces</c> of the chunk answer).
/// </summary>
/// <param name="Text">The line of the transcript.</param>
/// <param name="Own">What the piece of the line heard alone (<c>own_text</c>); null when the answer has no pieces
/// or no piece pairs with the line.</param>
/// <param name="Context">What the piece asked again with the audio around it gave (<c>context_text</c>); null when
/// it was not asked so.</param>
/// <param name="Was">The line as it stood before that answer replaced it (<c>whole_text_line</c>); null when it was
/// not replaced.</param>
public sealed record HeardLine(double Start, double End, string Text, string? Own, string? Context, string? Was);
