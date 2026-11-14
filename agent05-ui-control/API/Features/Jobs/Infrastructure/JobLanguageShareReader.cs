using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using XtractManager.Features.Jobs.Application;

namespace XtractManager.Features.Jobs.Infrastructure;

/// <summary>
/// Counts how much of a job's speech is Spanish and how much Russian (<see cref="LanguageShare"/>) from the
/// answers the transcriber left in the job folder. The folder is only read.
/// <list type="bullet">
/// <item>The lines are the segments of every <c>chunks_json/*.json</c>; the seconds of a line are
/// <c>end − start</c> of its segment.</item>
/// <item>A chunk the operator split and merged (<c>split_chunks/chunk_N/chunk_N_merged.md</c> exists) is counted
/// by the answers of its parts (<c>split_chunks/chunk_N/results/sub_chunk_*_result.json</c>, block
/// <c>raw_response</c>): those are the lines the transcript holds.</item>
/// <item>An answer with text and no segments (a plain model answered) is counted as lines without seconds.</item>
/// </list>
/// What the operator changed by hand in <c>chunks_md</c> or in the transcript is not seen here.
/// </summary>
public static class JobLanguageShareReader
{
    private static readonly Regex ChunkIndexInName = new(@"_part_(\d+)$", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>Null when the folder has no chunk answer with a line in it.</summary>
    public static LanguageShare? Read(string jobDirectoryPath)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(jobDirectoryPath))
                return null;
            var jsonDir = Path.Combine(jobDirectoryPath, "chunks_json");
            if (!Directory.Exists(jsonDir))
                return null;

            var share = new LanguageShare();
            foreach (var path in Directory.EnumerateFiles(jsonDir, "*.json").OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
            {
                var parts = PartAnswersOfMergedSplit(jobDirectoryPath, Path.GetFileNameWithoutExtension(path));
                if (parts.Count > 0)
                {
                    foreach (var part in parts)
                        AddAnswerFile(share, part, wrappedIn: "raw_response");
                }
                else
                {
                    AddAnswerFile(share, path, wrappedIn: null);
                }
            }

            return share.Lines > 0 ? share : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// The report of a job: its own share, or — for a dialogue (<c>dialogue.json</c>) — the share of each track
    /// read from the folder of the track's job, and their sum.
    /// </summary>
    /// <param name="jobDirectoryOf">Folder of a job by its id (the workspace).</param>
    public static JobLanguageShareReport ReadReport(string jobDirectoryPath, Func<string, string> jobDirectoryOf)
    {
        var manifest = DialogueManifestFile.TryLoad(jobDirectoryPath);
        if (manifest == null || manifest.Tracks.Count == 0)
            return new JobLanguageShareReport { Total = Read(jobDirectoryPath) };

        var report = new JobLanguageShareReport();
        var total = new LanguageShare();
        foreach (var track in manifest.Tracks)
        {
            // The manifest is a file on disk: an id in it that is not a plain folder name is not followed.
            var share = IsPlainFolderName(track.JobId) ? Read(jobDirectoryOf(track.JobId)) : null;
            if (share != null)
                total.Add(share);
            report.Tracks.Add(new TrackLanguageShare { JobId = track.JobId, Speaker = track.Speaker, Share = share });
        }

        report.Total = total.Lines > 0 ? total : null;
        return report;
    }

    private static List<string> PartAnswersOfMergedSplit(string jobDir, string chunkName)
    {
        var m = ChunkIndexInName.Match(chunkName);
        if (!m.Success || !int.TryParse(m.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var index))
            return new List<string>();

        var n = index.ToString(CultureInfo.InvariantCulture);
        var folder = Path.Combine(jobDir, "split_chunks", "chunk_" + n);
        var results = Path.Combine(folder, "results");
        if (!File.Exists(Path.Combine(folder, "chunk_" + n + "_merged.md")) || !Directory.Exists(results))
            return new List<string>();

        return Directory.EnumerateFiles(results, "sub_chunk_*_result.json")
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static void AddAnswerFile(LanguageShare share, string path, string? wrappedIn)
    {
        try
        {
            // The transcriber may be writing the file right now: share it instead of failing on a lock.
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var doc = JsonDocument.Parse(stream);
            var answer = doc.RootElement;
            if (wrappedIn != null)
            {
                if (answer.ValueKind != JsonValueKind.Object || !answer.TryGetProperty(wrappedIn, out answer))
                    return;
            }

            AddAnswer(share, answer);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // a file that cannot be read adds nothing; the rest of the job is still counted
        }
    }

    private static void AddAnswer(LanguageShare share, JsonElement answer)
    {
        if (answer.ValueKind != JsonValueKind.Object)
            return;

        if (answer.TryGetProperty("segments", out var segments)
            && segments.ValueKind == JsonValueKind.Array
            && segments.GetArrayLength() > 0)
        {
            foreach (var segment in segments.EnumerateArray())
            {
                if (segment.ValueKind != JsonValueKind.Object)
                    continue;
                var text = segment.TryGetProperty("text", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() : null;
                var start = Number(segment, "start");
                var end = Number(segment, "end");
                share.Add(text, start != null && end != null ? Math.Max(0, end.Value - start.Value) : null);
            }

            return;
        }

        if (answer.TryGetProperty("text", out var whole) && whole.ValueKind == JsonValueKind.String)
            share.Add(whole.GetString(), null);
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

    private static bool IsPlainFolderName(string id) =>
        !string.IsNullOrWhiteSpace(id)
        && id != "."
        && id != ".."
        && id.IndexOfAny(Path.GetInvalidFileNameChars()) < 0
        && string.Equals(Path.GetFileName(id), id, StringComparison.Ordinal);
}
