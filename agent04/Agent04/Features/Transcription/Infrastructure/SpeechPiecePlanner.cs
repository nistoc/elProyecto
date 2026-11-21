using Agent04.Features.Transcription.Application;

namespace Agent04.Features.Transcription.Infrastructure;

/// <summary>A stretch of a chunk that becomes one line of the transcript: speech between two real pauses.</summary>
/// <param name="StartSec">Where its first sound stands, in seconds of the chunk.</param>
/// <param name="EndSec">Where its last sound ends.</param>
/// <param name="SpeechSec">Sound inside it, without the short pauses it holds.</param>
internal sealed record SpeechStretch(double StartSec, double EndSec, double SpeechSec)
{
    public double DurationSec => EndSec - StartSec;
}

/// <summary>
/// Cuts a chunk into the pieces of <see cref="SpeechPiecesTranscription"/> by the silence found in it.
/// <para>
/// Sound between two silences is a run. Runs are joined into one piece while the pause between them is
/// short and the piece stays within the longest allowed length: a pause of a breath does not break a
/// phrase, a real pause does, and a line never grows longer than a reader can follow by its time mark.
/// </para>
/// </summary>
internal static class SpeechPiecePlanner
{
    /// <summary>
    /// A pause kept by silence compression is measured a few milliseconds shorter than it was written;
    /// it must still count as a pause of the full length.
    /// </summary>
    public const double PauseToleranceSec = 0.01;

    /// <summary>
    /// A piece longer than this many times the longest allowed length holds no pause the search saw: there is
    /// steady noise under the voice (a phone on a table, a fan, a bad line), and nothing in the recording is as
    /// quiet as the shared silence level. Such a chunk is searched again at a louder level
    /// (<see cref="PlanAtFittingLevel"/>). Seen live: 2.5 minutes as one line, no pause at −40 dB, 15–30
    /// pauses at −35 dB and as many long ones at −30 dB as a quiet recording has at −40 dB.
    /// </summary>
    public const double TooLongFactor = 2.0;

    /// <summary>Each new search counts this many dB louder as silence, at most <see cref="LouderSteps"/> times.</summary>
    public const double LouderStepDb = 5.0;

    public const int LouderSteps = 2;

    /// <summary>
    /// A louder search is taken only while its pieces keep at least this share of the chunk as sound: a level
    /// that turns a quiet voice into silence would lose words (a quiet recording keeps about 0.85 at −40 dB).
    /// </summary>
    public const double MinSoundShare = 0.6;

    /// <summary>
    /// The pieces of a chunk at the shared silence level, or at a louder one when the shared level finds no
    /// pause in it (see <see cref="TooLongFactor"/>). Returns the pieces and the level they were cut at.
    /// </summary>
    /// <param name="silenceAt">Silence found in the chunk at a level in dB.</param>
    /// <param name="noiseDb">The shared silence level.</param>
    public static (IReadOnlyList<SpeechStretch> Pieces, double NoiseDb) PlanAtFittingLevel(
        Func<double, IReadOnlyList<SilenceInterval>> silenceAt,
        double noiseDb,
        double durationSec,
        double pauseSec,
        double maxPieceSec,
        double minSpeechSec)
    {
        var pieces = Plan(silenceAt(noiseDb), durationSec, pauseSec, maxPieceSec, minSpeechSec);
        var level = noiseDb;
        for (var step = 1; step <= LouderSteps && Longest(pieces) > maxPieceSec * TooLongFactor; step++)
        {
            var louder = noiseDb + step * LouderStepDb;
            var again = Plan(silenceAt(louder), durationSec, pauseSec, maxPieceSec, minSpeechSec);
            if (again.Sum(p => p.SpeechSec) < durationSec * MinSoundShare)
                break;
            if (Longest(again) < Longest(pieces))
            {
                pieces = again;
                level = louder;
            }
        }

        return (pieces, level);
    }

    private static double Longest(IReadOnlyList<SpeechStretch> pieces) =>
        pieces.Count == 0 ? 0 : pieces.Max(p => p.DurationSec);

    /// <param name="silence">Silence found in the chunk, seconds of the chunk.</param>
    /// <param name="durationSec">Length of the chunk.</param>
    /// <param name="pauseSec">A pause this long always ends a piece.</param>
    /// <param name="maxPieceSec">A piece is ended at a shorter pause when it would grow longer than this.</param>
    /// <param name="minSpeechSec">A piece with less sound than this is noise, not speech; it is left out.</param>
    public static IReadOnlyList<SpeechStretch> Plan(
        IReadOnlyList<SilenceInterval> silence,
        double durationSec,
        double pauseSec,
        double maxPieceSec,
        double minSpeechSec)
    {
        var pieces = new List<SpeechStretch>();
        if (durationSec <= 0)
            return pieces;

        var runs = new List<(double Start, double End)>();
        var cursor = 0.0;
        foreach (var region in silence.OrderBy(r => r.StartSec))
        {
            var start = Math.Clamp(region.StartSec, 0, durationSec);
            var end = Math.Clamp(region.EndSec, 0, durationSec);
            if (start > cursor + 1e-6)
                runs.Add((cursor, start));
            cursor = Math.Max(cursor, end);
        }

        if (durationSec > cursor + 1e-6)
            runs.Add((cursor, durationSec));

        var open = false;
        double pieceStart = 0, pieceEnd = 0, speech = 0;
        foreach (var (start, end) in runs)
        {
            if (open)
            {
                var realPause = start - pieceEnd >= pauseSec - PauseToleranceSec;
                var tooLong = end - pieceStart > maxPieceSec;
                if (!realPause && !tooLong)
                {
                    pieceEnd = end;
                    speech += end - start;
                    continue;
                }

                Close();
            }

            open = true;
            pieceStart = start;
            pieceEnd = end;
            speech = end - start;
        }

        if (open)
            Close();
        return pieces;

        void Close()
        {
            if (speech >= minSpeechSec)
                pieces.Add(new SpeechStretch(pieceStart, pieceEnd, speech));
        }
    }
}
