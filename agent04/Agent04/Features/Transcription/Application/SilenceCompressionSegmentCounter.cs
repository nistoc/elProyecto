namespace Agent04.Features.Transcription.Application;

/// <summary>Estimates how many speech WAV extractions <see cref="IAudioUtils.WriteWavWithCompressedSilence"/> will run (same rules as AudioUtils).</summary>
public static class SilenceCompressionSegmentCounter
{
    /// <summary>ffmpeg rejects very small <c>-t</c>; float gaps between silence markers can be ~1e-5 s.</summary>
    public const double MinSpeechSegmentSec = 0.02;

    public static int CountSpeechExtractions(IReadOnlyList<SilenceInterval> intervals, double durationSeconds)
    {
        var cursor = 0.0;
        var count = 0;
        foreach (var iv in intervals)
        {
            if (iv.StartSec > cursor + 1e-9)
            {
                var speechDur = iv.StartSec - cursor;
                if (speechDur >= MinSpeechSegmentSec)
                    count++;
            }

            cursor = iv.EndSec;
        }

        var tailDur = durationSeconds - cursor;
        if (tailDur >= MinSpeechSegmentSec)
            count++;
        return count;
    }
}
