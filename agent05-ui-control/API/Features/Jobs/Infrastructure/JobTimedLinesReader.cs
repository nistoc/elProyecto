using System.Globalization;
using System.Text.Json;
using XtractManager.Features.Jobs.Application;

namespace XtractManager.Features.Jobs.Infrastructure;

/// <summary>
/// Reads the lines of a transcription job with their start and end on the timeline of the recording — the same
/// times the transcriber prints into the transcript, plus the end of each line. The folder is only read.
/// <list type="bullet">
/// <item>A segment of <c>chunks_json/X.json</c> stands at <c>offset_sec</c> of its chunk
/// (<c>timeline/chunks.json</c>); a segment that starts inside the chunk's <c>emit_guard_sec</c> repeats the
/// previous chunk and is left out, as in the transcript.</item>
/// <item>When pauses were cut out of the audio, <c>timeline/speech_map.json</c> puts the time back on the
/// recording (the rules of the transcriber's <c>SpeechTimeMap</c>).</item>
/// </list>
/// Not every job can be read this way: see <see cref="Read"/>.
/// </summary>
public static class JobTimedLinesReader
{
    /// <summary>The transcriber's <c>SpeechTimeMap.EarlyStartToleranceSec</c>: keep the two equal.</summary>
    public const double EarlyStartToleranceSec = 0.35;

    private const double Eps = 1e-6;

    /// <summary>Where a chunk stands in the audio that was split, and how much of its start repeats the previous chunk.</summary>
    public readonly record struct ChunkPlace(double OffsetSec, double EmitGuardSec);

    /// <summary>
    /// Null when the lines cannot be put on the recording timeline: the job has no <c>timeline/chunks.json</c>
    /// (a job from before that record), its audio was compressed without a speech map, or one of its chunks was
    /// split by the operator and merged (the parts have a timeline of their own, not read here).
    /// </summary>
    public static IReadOnlyList<TimedLine>? Read(string jobDirectoryPath)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(jobDirectoryPath))
                return null;
            var jsonDir = Path.Combine(jobDirectoryPath, "chunks_json");
            if (!Directory.Exists(jsonDir))
                return null;

            var places = LoadChunkPlaces(jobDirectoryPath);
            if (places == null)
                return null;

            var mapPath = Path.Combine(jobDirectoryPath, "timeline", "speech_map.json");
            RecordingTimeMap? map = null;
            if (File.Exists(mapPath))
            {
                map = RecordingTimeMap.TryLoad(mapPath);
                if (map == null)
                    return null;
            }
            else if (WasCompressed(jobDirectoryPath))
            {
                return null;
            }

            var lines = new List<TimedLine>();
            foreach (var path in Directory.EnumerateFiles(jsonDir, "*.json").OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
            {
                var chunkName = Path.GetFileNameWithoutExtension(path);
                if (!places.TryGetValue(chunkName, out var place) || JobLanguageShareReader.HasMergedSplit(jobDirectoryPath, chunkName))
                    return null;
                AddChunk(lines, path, place, map);
            }

            lines.Sort((a, b) => a.Start.CompareTo(b.Start));
            return lines;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// <c>timeline/chunks.json</c> by chunk name (the audio file name without its extension); null when the job
    /// has no such record.
    /// </summary>
    public static IReadOnlyDictionary<string, ChunkPlace>? LoadChunkPlaces(string jobDirectoryPath)
    {
        var path = Path.Combine(jobDirectoryPath, "timeline", "chunks.json");
        if (!File.Exists(path))
            return null;
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var doc = JsonDocument.Parse(stream);
            if (doc.RootElement.ValueKind != JsonValueKind.Object
                || !doc.RootElement.TryGetProperty("chunks", out var chunks)
                || chunks.ValueKind != JsonValueKind.Array)
                return null;

            var result = new Dictionary<string, ChunkPlace>(StringComparer.OrdinalIgnoreCase);
            foreach (var chunk in chunks.EnumerateArray())
            {
                if (chunk.ValueKind != JsonValueKind.Object
                    || !chunk.TryGetProperty("file", out var file)
                    || file.ValueKind != JsonValueKind.String)
                    continue;
                var name = Path.GetFileNameWithoutExtension(file.GetString() ?? "");
                if (name.Length == 0)
                    continue;
                result[name] = new ChunkPlace(Number(chunk, "offset_sec") ?? 0, Number(chunk, "emit_guard_sec") ?? 0);
            }

            return result;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>True for a segment the transcript leaves out: it starts inside the part that repeats the previous chunk.</summary>
    public static bool IsInsideGuard(double startSec, double endSec, double emitGuardSec) =>
        !IsWithoutTime(startSec, endSec) && startSec + Eps < emitGuardSec;

    /// <summary>A segment the model gave no time (a plain model answered): both marks are zero.</summary>
    public static bool IsWithoutTime(double startSec, double endSec) =>
        Math.Abs(startSec) < Eps && Math.Abs(endSec) < Eps;

    private static void AddChunk(List<TimedLine> lines, string path, ChunkPlace place, RecordingTimeMap? map)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var doc = JsonDocument.Parse(stream);
            if (doc.RootElement.ValueKind != JsonValueKind.Object
                || !doc.RootElement.TryGetProperty("segments", out var segments)
                || segments.ValueKind != JsonValueKind.Array)
                return;

            foreach (var segment in segments.EnumerateArray())
            {
                if (segment.ValueKind != JsonValueKind.Object)
                    continue;
                var text = segment.TryGetProperty("text", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() : null;
                var start = Number(segment, "start");
                var end = Number(segment, "end");
                if (start == null || end == null || LanguageShare.Classify(text) == LineLanguage.None)
                    continue;
                if (IsWithoutTime(start.Value, end.Value) || IsInsideGuard(start.Value, end.Value, place.EmitGuardSec))
                    continue;

                var from = start.Value + place.OffsetSec;
                var to = Math.Max(from, end.Value + place.OffsetSec);
                if (map != null)
                {
                    var sourceFrom = map.ToSourceSegmentStart(from, to);
                    to = Math.Max(sourceFrom, map.ToSourceEnd(to));
                    from = sourceFrom;
                }

                lines.Add(new TimedLine(from, to, text!.Trim()));
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // a chunk answer that cannot be read gives no lines
        }
    }

    /// <summary>Pauses were cut out of the audio (a compression report lies in <c>converted_wav/</c>).</summary>
    private static bool WasCompressed(string jobDir)
    {
        var converted = Path.Combine(jobDir, "converted_wav");
        return Directory.Exists(converted) && Directory.EnumerateFiles(converted, "*.silence_report.json").Any();
    }

    private static double? Number(JsonElement obj, string name)
    {
        if (!obj.TryGetProperty(name, out var value))
            return null;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number))
            return number;
        if (value.ValueKind == JsonValueKind.String
            && double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
            return parsed;
        return null;
    }

    /// <summary>
    /// "Position in the compressed file → position in the recording", from <c>timeline/speech_map.json</c>.
    /// The same rules as the transcriber's <c>SpeechTimeMap</c>, and one more for the end of a line.
    /// </summary>
    internal sealed class RecordingTimeMap
    {
        private readonly (double SourceStart, double SourceEnd, double OutputStart, double OutputEnd)[] _pieces;

        public RecordingTimeMap(IEnumerable<(double SourceStart, double SourceEnd, double OutputStart)> pieces)
        {
            _pieces = pieces
                .Select(p => (p.SourceStart, p.SourceEnd, p.OutputStart, p.OutputStart + (p.SourceEnd - p.SourceStart)))
                .OrderBy(p => p.OutputStart)
                .ToArray();
        }

        public static RecordingTimeMap? TryLoad(string path)
        {
            try
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var doc = JsonDocument.Parse(stream);
                if (doc.RootElement.ValueKind != JsonValueKind.Object
                    || !doc.RootElement.TryGetProperty("pieces", out var pieces)
                    || pieces.ValueKind != JsonValueKind.Array)
                    return null;

                var list = new List<(double, double, double)>();
                foreach (var piece in pieces.EnumerateArray())
                {
                    if (piece.ValueKind != JsonValueKind.Object)
                        continue;
                    var sourceStart = Number(piece, "source_start_sec");
                    var sourceEnd = Number(piece, "source_end_sec");
                    var outputStart = Number(piece, "output_start_sec");
                    if (sourceStart == null || sourceEnd == null || outputStart == null)
                        return null;
                    list.Add((sourceStart.Value, sourceEnd.Value, outputStart.Value));
                }

                return new RecordingTimeMap(list);
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
            {
                return null;
            }
        }

        /// <summary>A moment inside a gap (or before the first piece) is the start of the next piece; past the last piece it is its end.</summary>
        public double ToSourceTime(double outputTimeSec)
        {
            if (_pieces.Length == 0)
                return outputTimeSec;
            var index = FindPieceAtOrBefore(outputTimeSec);
            if (index < 0)
                return _pieces[0].SourceStart;
            var piece = _pieces[index];
            if (outputTimeSec <= piece.OutputEnd)
                return piece.SourceStart + (outputTimeSec - piece.OutputStart);
            return index + 1 < _pieces.Length ? _pieces[index + 1].SourceStart : piece.SourceEnd;
        }

        /// <summary>A segment that begins in the last moments of a piece and goes on into the next one starts with the next one.</summary>
        public double ToSourceSegmentStart(double outputStartSec, double outputEndSec)
        {
            if (_pieces.Length == 0)
                return outputStartSec;
            var index = FindPieceAtOrBefore(outputStartSec);
            if (index >= 0 && index + 1 < _pieces.Length)
            {
                var piece = _pieces[index];
                var next = _pieces[index + 1];
                var insidePiece = outputStartSec <= piece.OutputEnd;
                if (insidePiece && piece.OutputEnd - outputStartSec < EarlyStartToleranceSec && outputEndSec > next.OutputStart)
                    return next.SourceStart;
            }

            return ToSourceTime(outputStartSec);
        }

        /// <summary>The end of a line: a moment inside a gap is the end of the piece before it — that is where the sound stopped.</summary>
        public double ToSourceEnd(double outputTimeSec)
        {
            if (_pieces.Length == 0)
                return outputTimeSec;
            var index = FindPieceAtOrBefore(outputTimeSec);
            if (index < 0)
                return _pieces[0].SourceStart;
            var piece = _pieces[index];
            return outputTimeSec <= piece.OutputEnd
                ? piece.SourceStart + (outputTimeSec - piece.OutputStart)
                : piece.SourceEnd;
        }

        private int FindPieceAtOrBefore(double outputTimeSec)
        {
            int lo = 0, hi = _pieces.Length - 1, found = -1;
            while (lo <= hi)
            {
                var mid = (lo + hi) / 2;
                if (_pieces[mid].OutputStart <= outputTimeSec)
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
}
