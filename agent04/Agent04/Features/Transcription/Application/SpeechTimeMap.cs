namespace Agent04.Features.Transcription.Application;

/// <summary>
/// One stretch of the source audio that silence compression kept, and where it starts in the compressed file.
/// </summary>
public sealed record SpeechPiece(double SourceStartSec, double SourceEndSec, double OutputStartSec)
{
    public double DurationSec => SourceEndSec - SourceStartSec;
    public double OutputEndSec => OutputStartSec + DurationSec;
}

/// <summary>One step of assembling the compressed file: a kept piece of the source or a short gap of silence.</summary>
public sealed record SpeechCompressionStep(SpeechPiece? Piece)
{
    public bool IsGap => Piece is null;
}

/// <summary>
/// The table "position in the compressed file ↔ position in the source audio". The model hears the compressed
/// file, so its times are on that scale; this map puts them back on the timeline of the recording.
/// </summary>
public sealed class SpeechTimeMap
{
    /// <summary>
    /// The model often marks a phrase a little before its first sound. When a segment begins in the last
    /// moments of one piece and goes on into the next, its start belongs to the next piece — otherwise the
    /// line would land before the whole pause that was cut out. The tolerance is the size of that error
    /// seen on real lessons (0.3–0.5 s median).
    /// </summary>
    public const double EarlyStartToleranceSec = 0.35;

    private readonly SpeechPiece[] _pieces;

    public SpeechTimeMap(IEnumerable<SpeechPiece> pieces)
    {
        _pieces = pieces.OrderBy(p => p.OutputStartSec).ToArray();
    }

    /// <summary>Kept pieces in the order they stand in the compressed file.</summary>
    public IReadOnlyList<SpeechPiece> Pieces => _pieces;

    /// <summary>
    /// The order in which the compressed file is assembled: for every silence region the piece before it
    /// (when long enough) and then a gap of <paramref name="keepSilenceSec"/>; the tail after the last region.
    /// This is the single description of the layout: the file is built from these steps and the map is
    /// their pieces.
    /// </summary>
    public static IReadOnlyList<SpeechCompressionStep> Plan(
        IReadOnlyList<SilenceInterval> silence,
        double sourceDurationSec,
        double keepSilenceSec,
        double minPieceSec)
    {
        var steps = new List<SpeechCompressionStep>();
        var keep = keepSilenceSec > 1e-6 ? keepSilenceSec : 0.0;
        var cursor = 0.0;
        var output = 0.0;

        foreach (var region in silence)
        {
            if (region.StartSec > cursor + 1e-9)
            {
                var duration = region.StartSec - cursor;
                if (duration >= minPieceSec)
                {
                    steps.Add(new SpeechCompressionStep(new SpeechPiece(cursor, region.StartSec, output)));
                    output += duration;
                }
            }

            if (keep > 0)
            {
                steps.Add(new SpeechCompressionStep(null));
                output += keep;
            }

            cursor = region.EndSec;
        }

        if (sourceDurationSec - cursor >= minPieceSec)
            steps.Add(new SpeechCompressionStep(new SpeechPiece(cursor, sourceDurationSec, output)));

        return steps;
    }

    /// <summary>
    /// Source time of a moment in the compressed file. A moment inside a gap (or before the first piece)
    /// is the start of the next piece: that is where sound begins again. Past the last piece it is its end.
    /// </summary>
    public double ToSourceTime(double outputTimeSec)
    {
        if (_pieces.Length == 0)
            return outputTimeSec;

        var index = FindPieceAtOrBefore(outputTimeSec);
        if (index < 0)
            return _pieces[0].SourceStartSec;

        var piece = _pieces[index];
        if (outputTimeSec <= piece.OutputEndSec)
            return piece.SourceStartSec + (outputTimeSec - piece.OutputStartSec);

        return index + 1 < _pieces.Length ? _pieces[index + 1].SourceStartSec : piece.SourceEndSec;
    }

    /// <summary>
    /// Source time of the start of a segment that the model placed at
    /// [<paramref name="outputStartSec"/>, <paramref name="outputEndSec"/>] in the compressed file.
    /// See <see cref="EarlyStartToleranceSec"/> for the one correction it makes.
    /// </summary>
    public double ToSourceSegmentStart(double outputStartSec, double outputEndSec)
    {
        if (_pieces.Length == 0)
            return outputStartSec;

        var index = FindPieceAtOrBefore(outputStartSec);
        if (index >= 0 && index + 1 < _pieces.Length)
        {
            var piece = _pieces[index];
            var next = _pieces[index + 1];
            var insidePiece = outputStartSec <= piece.OutputEndSec;
            var leftInPiece = piece.OutputEndSec - outputStartSec;
            if (insidePiece && leftInPiece < EarlyStartToleranceSec && outputEndSec > next.OutputStartSec)
                return next.SourceStartSec;
        }

        return ToSourceTime(outputStartSec);
    }

    /// <summary>Index of the last piece that starts at or before the moment; -1 when the moment is before all pieces.</summary>
    private int FindPieceAtOrBefore(double outputTimeSec)
    {
        var lo = 0;
        var hi = _pieces.Length - 1;
        var found = -1;
        while (lo <= hi)
        {
            var mid = (lo + hi) / 2;
            if (_pieces[mid].OutputStartSec <= outputTimeSec)
            {
                found = mid;
                lo = mid + 1;
            }
            else
            {
                hi = mid - 1;
            }
        }
        return found;
    }
}
