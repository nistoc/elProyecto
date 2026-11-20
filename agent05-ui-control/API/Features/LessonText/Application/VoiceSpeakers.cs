namespace XtractManager.Features.LessonText.Application;

/// <summary>A stretch of a recording one voice spoke, in seconds of the audio file.</summary>
/// <param name="Speaker">The voice as the model of voices named it (<c>S1</c>, <c>S2</c>…, most time first).</param>
public sealed record VoiceSegment(double Start, double End, string Speaker);

/// <summary>
/// Who speaks when in a recording, told by voice on the local GPU (the tool <c>voice-speakers/</c> of the
/// repository), kept by the job whose audio it read. No text: only times and voices.
/// </summary>
public interface IVoiceSpeakersStore
{
    /// <returns>The stretches in the order of their start; null when the job has no answer or it cannot be read.</returns>
    IReadOnlyList<VoiceSegment>? For(string jobId);
}

/// <summary>
/// Voices told by sound rather than by a model that also writes the text. Measured 2026-11-20 on a recording
/// with a track per participant: 93.9 % of words given the right person by voice alone, 97.7 % with the labels of a
/// diarize run named by voice (<see cref="DiarizedSpeakers.TieByVoice"/>) — as many as the diarize run tied by
/// the words its chunks share, without its weak point: a chunk where the model named one person anew no longer
/// carries the error into every chunk after it.
/// </summary>
public static class VoiceSpeakers
{
    /// <summary>How far from a word a voice may stand and still be its voice.</summary>
    public const double NearSec = 0.5;

    public const string NamePrefix = "voice_";

    /// <summary>The name a voice gets in the lesson text: <c>S2</c> → <c>voice_2</c>.</summary>
    public static string Name(string voice) =>
        voice.Length > 1 && (voice[0] == 'S' || voice[0] == 's') && voice[1..].All(char.IsDigit)
            ? NamePrefix + voice[1..]
            : voice;

    /// <summary>
    /// A line of ours cut where the voice changes. The line knows only its start; it is taken to end where the
    /// voice that began before the next line ends (<see cref="NextLineMarginSec"/>), so a pause after it does not
    /// pull its last words into the next speaker. Its words are spread evenly over that time, each takes the voice
    /// at its time, and a stretch of one or two words is not a turn of its own (as in <see cref="SpeakerOverlay"/>).
    /// </summary>
    /// <param name="voice">The stretches in the order of their start.</param>
    /// <param name="nextStart">Start of the next line; null for the last one.</param>
    public static IReadOnlyList<SpokenPart> Split(string text, double start, double? nextStart, IReadOnlyList<VoiceSegment> voice)
    {
        var tokens = (text ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0)
            return Array.Empty<SpokenPart>();

        var limit = Math.Min(nextStart ?? start + SpeakerOverlay.MaxLineSec, start + SpeakerOverlay.MaxLineSec);
        var end = start;
        foreach (var v in voice)
        {
            if (v.Start >= limit - NextLineMarginSec)
                break;
            if (v.End > start - NearSec)
                end = Math.Max(end, Math.Min(v.End, limit));
        }

        var speakers = new string?[tokens.Length];
        for (var k = 0; k < tokens.Length; k++)
            speakers[k] = At(voice, start + (end - start) * (k + 0.5) / tokens.Length);
        if (speakers.All(s => s == null))
            return new[] { new SpokenPart(null, start, string.Join(' ', tokens), tokens.Length, 0) };

        // a word with no voice near it takes the voice of the word before it, else of the one after it
        for (var k = 1; k < tokens.Length; k++)
            speakers[k] ??= speakers[k - 1];
        for (var k = tokens.Length - 2; k >= 0; k--)
            speakers[k] ??= speakers[k + 1];

        var named = speakers.Select(s => s!).ToArray();
        var matched = Enumerable.Repeat(-1, tokens.Length).ToArray();
        SpeakerOverlay.Smooth(named, matched);
        SpeakerOverlay.SnapToSentences(tokens, named, matched);

        var parts = new List<SpokenPart>();
        var from = 0;
        for (var k = 1; k <= tokens.Length; k++)
        {
            if (k < tokens.Length && named[k] == named[from])
                continue;
            var at = from == 0 ? start : Math.Round(start + (end - start) * from / tokens.Length, 2);
            parts.Add(new SpokenPart(named[from], at, string.Join(' ', tokens, from, k - from), k - from, 0));
            from = k;
        }

        return parts;
    }

    /// <summary>A voice that begins this close before the next line is taken for the start of the next line.</summary>
    public const double NextLineMarginSec = 0.3;

    /// <summary>The voice at a time: the stretch it falls in, else the nearest within <see cref="NearSec"/>; else null.</summary>
    /// <param name="voice">The stretches in the order of their start.</param>
    public static string? At(IReadOnlyList<VoiceSegment> voice, double time)
    {
        int lo = 0, hi = voice.Count;
        while (lo < hi)
        {
            var mid = (lo + hi) / 2;
            if (voice[mid].Start <= time)
                lo = mid + 1;
            else
                hi = mid;
        }

        string? best = null;
        var bestGap = NearSec;
        for (var i = Math.Max(0, lo - 2); i < Math.Min(voice.Count, lo + 1); i++)
        {
            var v = voice[i];
            var gap = time < v.Start ? v.Start - time : time > v.End ? time - v.End : 0;
            if (gap <= bestGap)
            {
                bestGap = gap;
                best = v.Speaker;
            }
        }

        return best == null ? null : Name(best);
    }
}
