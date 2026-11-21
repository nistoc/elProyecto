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
    public static IReadOnlyList<TimedLine>? Read(string jobDirectoryPath) =>
        ReadLines(jobDirectoryPath)?.Select(l => new TimedLine(l.From, l.To, l.Text)).ToList();

    /// <summary>
    /// The lines of <see cref="Read"/>, each with what the piece of the line heard alone, asked again with the
    /// audio around it, and the line it replaced (<see cref="HeardLine"/>). A line pairs with the piece whose end
    /// is its end (the transcriber writes a line per piece that got text, in piece order), else with the piece
    /// that shares more than half of its time. Null when <see cref="Read"/> is null.
    /// </summary>
    public static IReadOnlyList<HeardLine>? ReadHeard(string jobDirectoryPath) =>
        ReadLines(jobDirectoryPath)?.Select(l => new HeardLine(l.From, l.To, l.Text, l.Piece?.Own, l.Piece?.Context, l.Piece?.Was)).ToList();

    /// <summary>A piece of a chunk of the plain-text model: an item of block <c>speech_pieces</c>.</summary>
    private sealed record Piece(double Start, double End, string? Own, string? Context, string? Was);

    private readonly record struct Line(double From, double To, string Text, Piece? Piece);

    private static List<Line>? ReadLines(string jobDirectoryPath)
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

            var lines = new List<Line>();
            foreach (var path in Directory.EnumerateFiles(jsonDir, "*.json").OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
            {
                var chunkName = Path.GetFileNameWithoutExtension(path);
                if (!places.TryGetValue(chunkName, out var place) || JobLanguageShareReader.HasMergedSplit(jobDirectoryPath, chunkName))
                    return null;
                AddChunk(lines, path, place, map);
            }

            lines.Sort((a, b) => a.From.CompareTo(b.From));
            return lines;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// The lines of a job transcribed by a model that tells speakers apart, each with the speaker as the model
    /// named it in its chunk (<see cref="DiarizedSegment"/>). Unlike <see cref="Read"/>, the lines that repeat
    /// the previous chunk are kept and marked: the same speech heard by two chunks ties their speakers. A chunk
    /// the server refused whole and that was asked in parts (block <c>auto_split</c> of its answer) gives one
    /// unit per part, since every part named its speakers on its own. Null when <see cref="Read"/> is null, and
    /// when no line names a speaker.
    /// </summary>
    public static IReadOnlyList<DiarizedSegment>? ReadDiarized(string jobDirectoryPath)
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

            var segments = new List<DiarizedSegment>();
            var unit = 0;
            foreach (var path in Directory.EnumerateFiles(jsonDir, "*.json").OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
            {
                var chunkName = Path.GetFileNameWithoutExtension(path);
                if (!places.TryGetValue(chunkName, out var place) || JobLanguageShareReader.HasMergedSplit(jobDirectoryPath, chunkName))
                    return null;
                unit = AddDiarizedChunk(segments, path, place, map, unit);
            }

            return segments.Count == 0 ? null : segments;
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

    private static void AddChunk(List<Line> lines, string path, ChunkPlace place, RecordingTimeMap? map)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var doc = JsonDocument.Parse(stream);
            if (doc.RootElement.ValueKind != JsonValueKind.Object
                || !doc.RootElement.TryGetProperty("segments", out var segments)
                || segments.ValueKind != JsonValueKind.Array)
                return;

            var pieces = Pieces(doc.RootElement);
            var nextPiece = 0;
            foreach (var segment in segments.EnumerateArray())
            {
                if (segment.ValueKind != JsonValueKind.Object)
                    continue;
                var text = segment.TryGetProperty("text", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() : null;
                var start = Number(segment, "start");
                var end = Number(segment, "end");
                if (start == null || end == null)
                    continue;
                // Paired before a line is left out, so that the lines after it find their own pieces.
                var piece = PieceOf(pieces, ref nextPiece, start.Value, end.Value);
                if (LanguageShare.Classify(text) == LineLanguage.None)
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

                lines.Add(new Line(from, to, text!.Trim(), piece));
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // a chunk answer that cannot be read gives no lines
        }
    }

    /// <summary>The items of block <c>speech_pieces</c>, in their order; empty for an answer of another model.</summary>
    private static List<Piece> Pieces(JsonElement answer)
    {
        var pieces = new List<Piece>();
        if (!answer.TryGetProperty("speech_pieces", out var block) || block.ValueKind != JsonValueKind.Object
            || !block.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
            return pieces;
        foreach (var item in items.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object || Number(item, "start") is not { } start || Number(item, "end") is not { } end)
                continue;
            pieces.Add(new Piece(start, end, Text(item, "own_text"), Text(item, "context_text"), Text(item, "whole_text_line")));
        }

        return pieces;
    }

    /// <summary>
    /// The piece of a line: the next one whose end is the line's end, else the one that shares more than half
    /// of the line's time.
    /// </summary>
    private static Piece? PieceOf(List<Piece> pieces, ref int next, double start, double end)
    {
        if (pieces.Count == 0)
            return null;
        for (var k = next; k < pieces.Count; k++)
        {
            if (Math.Abs(pieces[k].End - end) <= 0.002)
            {
                next = k + 1;
                return pieces[k];
            }
        }

        Piece? best = null;
        var shared = 0.0;
        foreach (var piece in pieces)
        {
            var overlap = Math.Min(end, piece.End) - Math.Max(start, piece.Start);
            if (overlap > shared)
            {
                best = piece;
                shared = overlap;
            }
        }

        return shared > 0.5 * Math.Max(0.01, end - start) ? best : null;
    }

    private static string? Text(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && value.GetString() is { } text
            && text.Trim() is { Length: > 0 } trimmed
            ? trimmed
            : null;

    /// <returns>The number of the unit after the chunk's last one.</returns>
    private static int AddDiarizedChunk(List<DiarizedSegment> segments, string path, ChunkPlace place, RecordingTimeMap? map, int firstUnit)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var doc = JsonDocument.Parse(stream);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return firstUnit + 1;

            // Parts of a chunk the server refused whole: where each begins inside the chunk.
            var partStarts = new List<double>();
            if (root.TryGetProperty("auto_split", out var split) && split.ValueKind == JsonValueKind.Object
                && split.TryGetProperty("parts", out var parts) && parts.ValueKind == JsonValueKind.Array)
            {
                foreach (var part in parts.EnumerateArray())
                {
                    if (part.ValueKind == JsonValueKind.Object && Number(part, "start_sec") is { } partStart)
                        partStarts.Add(partStart);
                }

                partStarts.Sort();
            }

            var units = Math.Max(1, partStarts.Count);
            if (!root.TryGetProperty("segments", out var list) || list.ValueKind != JsonValueKind.Array)
                return firstUnit + units;

            foreach (var segment in list.EnumerateArray())
            {
                if (segment.ValueKind != JsonValueKind.Object)
                    continue;
                var text = segment.TryGetProperty("text", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() : null;
                var speaker = segment.TryGetProperty("speaker", out var s) && s.ValueKind == JsonValueKind.String ? s.GetString()?.Trim() : null;
                var start = Number(segment, "start");
                var end = Number(segment, "end");
                if (string.IsNullOrEmpty(speaker) || start == null || end == null || LanguageShare.Classify(text) == LineLanguage.None)
                    continue;
                if (IsWithoutTime(start.Value, end.Value))
                    continue;

                var part = 0;
                while (part + 1 < partStarts.Count && start.Value + Eps >= partStarts[part + 1])
                    part++;

                var from = start.Value + place.OffsetSec;
                var to = Math.Max(from, end.Value + place.OffsetSec);
                if (map != null)
                {
                    var sourceFrom = map.ToSourceSegmentStart(from, to);
                    to = Math.Max(sourceFrom, map.ToSourceEnd(to));
                    from = sourceFrom;
                }

                segments.Add(new DiarizedSegment(
                    firstUnit + part, from, to, speaker, text!.Trim(),
                    IsInsideGuard(start.Value, end.Value, place.EmitGuardSec)));
            }

            return firstUnit + units;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // a chunk answer that cannot be read gives no lines
            return firstUnit + 1;
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
