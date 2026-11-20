using System.Globalization;
using System.Text.Json;
using XtractManager.Features.LessonText.Application;

namespace XtractManager.Features.LessonText.Infrastructure;

/// <summary>One segment of an answer of another recognition service.</summary>
public sealed record OtherServiceSegment(double Start, double End, string Speaker, string Text);

/// <summary>
/// An answer file of another recognition service that lies in a day folder next to the audio it was made
/// from: <c>{ segments: [{ start_time, end_time, text, speaker: { id }, words: [{ start_time, end_time, text }] }] }</c>.
/// It names the speakers, which a transcript of ours made from a mixed recording by the plain model does not.
/// The file is only read.
/// </summary>
public sealed class OtherServiceAnswer
{
    private OtherServiceAnswer(IReadOnlyList<OtherServiceSegment> segments, IReadOnlyList<HeardWord> words)
    {
        Segments = segments;
        Words = words;
    }

    public IReadOnlyList<OtherServiceSegment> Segments { get; }

    /// <summary>Every word with the speaker of its segment, in the order of its start.</summary>
    public IReadOnlyList<HeardWord> Words { get; }

    /// <summary>
    /// The answer file of an audio file among <paramref name="answers"/>: the one whose name begins with the
    /// name of the audio (letters and digits only: "Voice 250806_191245.m4a" has "Voice_250806_191245_rus.json"
    /// and "Voice 250806_191245.m4a.json"), or the only answer of a day that has only this audio.
    /// </summary>
    public static FileInfo? For(string audioFileName, IReadOnlyList<FileInfo> answers, int audioFilesOfDay)
    {
        var audio = Plain(Path.GetFileNameWithoutExtension(audioFileName));
        if (audio.Length > 0)
        {
            var byName = answers.Where(a => Plain(a.Name).StartsWith(audio, StringComparison.Ordinal)).ToList();
            if (byName.Count == 1)
                return byName[0];
            if (byName.Count > 1)
                return null;
        }

        return answers.Count == 1 && audioFilesOfDay == 1 ? answers[0] : null;
    }

    /// <summary>Null when the file is not such an answer or cannot be read.</summary>
    public static OtherServiceAnswer? TryRead(FileInfo file)
    {
        try
        {
            using var stream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var doc = JsonDocument.Parse(stream);
            if (doc.RootElement.ValueKind != JsonValueKind.Object
                || !doc.RootElement.TryGetProperty("segments", out var list)
                || list.ValueKind != JsonValueKind.Array)
                return null;

            var segments = new List<OtherServiceSegment>();
            var words = new List<HeardWord>();
            foreach (var segment in list.EnumerateArray())
            {
                if (segment.ValueKind != JsonValueKind.Object)
                    continue;
                var start = Number(segment, "start_time");
                var end = Number(segment, "end_time");
                var speaker = segment.TryGetProperty("speaker", out var who) && who.ValueKind == JsonValueKind.Object
                              && who.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String
                    ? id.GetString()
                    : null;
                if (start == null || string.IsNullOrWhiteSpace(speaker))
                    continue;
                var text = segment.TryGetProperty("text", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() ?? "" : "";
                var to = Math.Max(start.Value, end ?? start.Value);
                segments.Add(new OtherServiceSegment(start.Value, to, speaker!, text.Trim()));

                var before = words.Count;
                if (segment.TryGetProperty("words", out var inner) && inner.ValueKind == JsonValueKind.Array)
                {
                    foreach (var word in inner.EnumerateArray())
                    {
                        if (word.ValueKind != JsonValueKind.Object)
                            continue;
                        var from = Number(word, "start_time");
                        var wordText = word.TryGetProperty("text", out var wt) && wt.ValueKind == JsonValueKind.String ? wt.GetString() : null;
                        if (from == null || string.IsNullOrWhiteSpace(wordText))
                            continue;
                        words.Add(new HeardWord(from.Value, Math.Max(from.Value, Number(word, "end_time") ?? from.Value), wordText!.Trim(), speaker!));
                    }
                }

                // A segment without its words still tells who spoke when.
                if (words.Count == before)
                    words.Add(new HeardWord(start.Value, to, text.Trim(), speaker!));
            }

            if (segments.Count == 0)
                return null;
            words.Sort((a, b) => a.Start.CompareTo(b.Start));
            return new OtherServiceAnswer(segments, words);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string Plain(string name) =>
        string.Concat(name.Where(char.IsLetterOrDigit)).ToLowerInvariant();

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
}
