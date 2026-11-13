using System.Globalization;
using System.Text.RegularExpressions;
using XtractManager.Features.Sessions.Application;

namespace XtractManager.Features.Sessions.Infrastructure;

/// <summary>
/// Reads the recordings folder into lessons, periods and tracks. Layouts it knows:
/// <list type="bullet">
/// <item>day folder <c>YYYY.MM.DD[ title]</c> with Zoom period folders <c>YYYY-MM-DD HH.mm.ss title</c>:
/// mixed audio in the folder, participant tracks in <c>Audio Record</c>;</item>
/// <item>day folder with flat audio files (older recordings): every audio file is a period with one mixed track;</item>
/// <item>Zoom period folders left in the root: grouped by date into a lesson marked as loose.</item>
/// </list>
/// Everything else in the root (books, the repository) is skipped. The scanner only reads.
/// </summary>
public static class RecordingsScanner
{
    public const string StateReady = "ready";
    public const string StateUnconverted = "unconverted";
    public const string StateEmpty = "empty";

    public const string KindMixed = "mixed";
    public const string KindParticipant = "participant";

    public const string LooseSessionPrefix = "@loose/";
    public const string ParticipantFolderName = "Audio Record";

    private static readonly string[] AudioExtensions = [".m4a", ".mp3", ".wav", ".ogg", ".flac"];

    private static readonly Regex DayFolder = new(
        @"^(?<date>\d{4}\.\d{2}\.\d{2})(?:[\s\-–—]+(?<title>\S.*))?$", RegexOptions.CultureInvariant);

    private static readonly Regex PeriodFolder = new(
        @"^(?<date>\d{4}-\d{2}-\d{2}) (?<time>\d{2}\.\d{2}\.\d{2})(?:\s+(?<title>\S.*))?$", RegexOptions.CultureInvariant);

    private static readonly Regex LongStamp = new(
        @"(?<!\d)(?<date>\d{4}-\d{2}-\d{2}) (?<time>\d{2}\.\d{2}\.\d{2})(?!\d)", RegexOptions.CultureInvariant);

    private static readonly Regex ShortStamp = new(
        @"(?<!\d)(?<date>\d{6})_(?<time>\d{6})(?!\d)", RegexOptions.CultureInvariant);

    private static readonly Regex MixedStem = new(@"^audio(?<id>\d+)$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    private static readonly Regex TrailingRecordingId = new(@"\d{9,11}$", RegexOptions.CultureInvariant);
    private static readonly Regex WordBorder = new(@"(?<=\p{Ll})(?=\p{Lu})", RegexOptions.CultureInvariant);

    public static bool IsAudioFile(string fileName) =>
        AudioExtensions.Contains(Path.GetExtension(fileName), StringComparer.OrdinalIgnoreCase);

    /// <param name="rootPath">The recordings folder.</param>
    /// <param name="duration">Length of an audio file in seconds, or null when it cannot be read.</param>
    public static IReadOnlyList<RecordingSession> Scan(string rootPath, Func<FileInfo, double?>? duration = null)
    {
        duration ??= _ => null;
        var root = new DirectoryInfo(rootPath);
        if (!root.Exists)
            return Array.Empty<RecordingSession>();

        var sessions = new List<RecordingSession>();
        var loose = new SortedDictionary<string, List<RecordingPeriod>>(StringComparer.Ordinal);

        foreach (var dir in SafeDirectories(root))
        {
            var day = DayFolder.Match(dir.Name);
            if (day.Success && TryDate(day.Groups["date"].Value, "yyyy.MM.dd", out var dayDate))
            {
                var title = day.Groups["title"].Success ? day.Groups["title"].Value.Trim() : null;
                sessions.Add(new RecordingSession(
                    dir.Name,
                    dayDate,
                    string.IsNullOrEmpty(title) ? null : title,
                    Loose: false,
                    ScanDay(dir, duration)));
                continue;
            }

            var period = PeriodFolder.Match(dir.Name);
            if (period.Success && TryDate(period.Groups["date"].Value, "yyyy-MM-dd", out var looseDate))
            {
                if (!loose.TryGetValue(looseDate, out var list))
                    loose[looseDate] = list = new List<RecordingPeriod>();
                list.Add(ScanPeriodFolder(dir, dir.Name, period, duration));
            }
        }

        foreach (var (date, periods) in loose)
            sessions.Add(new RecordingSession(LooseSessionPrefix + date, date, null, Loose: true, SortPeriods(periods)));

        return sessions
            .OrderByDescending(s => s.Date, StringComparer.Ordinal)
            .ThenBy(s => s.Loose ? 0 : 1)
            .ThenBy(s => s.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IReadOnlyList<RecordingPeriod> ScanDay(DirectoryInfo day, Func<FileInfo, double?> duration)
    {
        var periods = new List<RecordingPeriod>();

        foreach (var dir in SafeDirectories(day))
        {
            var m = PeriodFolder.Match(dir.Name);
            if (m.Success && TryDate(m.Groups["date"].Value, "yyyy-MM-dd", out _))
                periods.Add(ScanPeriodFolder(dir, day.Name + "/" + dir.Name, m, duration));
        }

        var files = SafeFiles(day);
        foreach (var audio in files.Where(f => IsAudioFile(f.Name)))
        {
            var stem = Path.GetFileNameWithoutExtension(audio.Name);
            var id = day.Name + "/" + audio.Name;
            var hasVideo = files.Any(f =>
                f.Extension.Equals(".mp4", StringComparison.OrdinalIgnoreCase)
                && Path.GetFileNameWithoutExtension(f.Name).Trim().Equals(stem.Trim(), StringComparison.OrdinalIgnoreCase));
            var hasOldTranscript = stem.Trim().Length > 0 && files.Any(f =>
                f.Extension.Equals(".md", StringComparison.OrdinalIgnoreCase)
                && f.Name.StartsWith(stem.Trim(), StringComparison.OrdinalIgnoreCase)
                && f.Name.Contains("transcript", StringComparison.OrdinalIgnoreCase));

            periods.Add(new RecordingPeriod(
                id,
                StartFromFileStem(stem),
                audio.Name,
                StateReady,
                hasVideo,
                HasChat: false,
                HasSubtitles: false,
                hasOldTranscript,
                RawZoomBytes: 0,
                new[] { new RecordingTrack(id, KindMixed, null, audio.Name, audio.Length, duration(audio)) }));
        }

        return SortPeriods(periods);
    }

    private static RecordingPeriod ScanPeriodFolder(
        DirectoryInfo dir,
        string id,
        Match name,
        Func<FileInfo, double?> duration)
    {
        var files = SafeFiles(dir);
        var tracks = new List<RecordingTrack>();
        string? recordingId = null;

        foreach (var audio in files.Where(f => IsAudioFile(f.Name)).OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase))
        {
            var mixed = MixedStem.Match(Path.GetFileNameWithoutExtension(audio.Name));
            if (mixed.Success)
                recordingId ??= mixed.Groups["id"].Value;
            tracks.Add(new RecordingTrack(id + "/" + audio.Name, KindMixed, null, audio.Name, audio.Length, duration(audio)));
        }

        var participantDir = SafeDirectories(dir)
            .FirstOrDefault(d => d.Name.Equals(ParticipantFolderName, StringComparison.OrdinalIgnoreCase));
        if (participantDir != null)
        {
            foreach (var audio in SafeFiles(participantDir)
                         .Where(f => IsAudioFile(f.Name))
                         .OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase))
            {
                tracks.Add(new RecordingTrack(
                    id + "/" + participantDir.Name + "/" + audio.Name,
                    KindParticipant,
                    ParticipantName(Path.GetFileNameWithoutExtension(audio.Name), recordingId),
                    audio.Name,
                    audio.Length,
                    duration(audio)));
            }
        }

        var rawZoomBytes = files
            .Where(f => f.Extension.Equals(".zoom", StringComparison.OrdinalIgnoreCase))
            .Sum(f => f.Length);
        var state = tracks.Count > 0 ? StateReady : rawZoomBytes > 0 ? StateUnconverted : StateEmpty;
        var title = name.Groups["title"].Success ? name.Groups["title"].Value.Trim() : null;

        return new RecordingPeriod(
            id,
            Stamp(name.Groups["date"].Value, name.Groups["time"].Value.Replace('.', ':')),
            string.IsNullOrEmpty(title) ? null : title,
            state,
            HasVideo: files.Any(f => f.Extension.Equals(".mp4", StringComparison.OrdinalIgnoreCase)),
            HasChat: files.Any(f => f.Name.Equals("chat.txt", StringComparison.OrdinalIgnoreCase)),
            HasSubtitles: files.Any(f => f.Extension.Equals(".vtt", StringComparison.OrdinalIgnoreCase)),
            HasOldTranscript: false,
            rawZoomBytes,
            tracks);
    }

    /// <summary>
    /// Name of the participant out of a Zoom track file stem <c>audio{Name}{N}{recording id}</c>,
    /// for example <c>audioMariaLopez11844996676</c> → <c>Maria Lopez</c>.
    /// </summary>
    /// <param name="recordingId">Digits of the mixed file of the same period (<c>audio{recording id}.m4a</c>), when known.</param>
    public static string? ParticipantName(string fileStem, string? recordingId)
    {
        var s = (fileStem ?? "").Trim();
        if (s.StartsWith("audio", StringComparison.OrdinalIgnoreCase))
            s = s[5..];

        if (!string.IsNullOrEmpty(recordingId) && s.EndsWith(recordingId, StringComparison.Ordinal))
        {
            s = s[..^recordingId.Length];
        }
        else
        {
            var id = TrailingRecordingId.Match(s);
            if (id.Success)
                s = s[..id.Index];
        }

        // The digit before the recording id is the number Zoom gave the participant.
        if (s.Length > 1 && char.IsDigit(s[^1]))
            s = s[..^1];

        s = WordBorder.Replace(s, " ").Trim();
        return s.Length == 0 ? null : s;
    }

    private static string? StartFromFileStem(string stem)
    {
        var m = LongStamp.Match(stem);
        if (m.Success && TryDate(m.Groups["date"].Value, "yyyy-MM-dd", out var date))
            return Stamp(date, m.Groups["time"].Value.Replace('.', ':'));

        m = ShortStamp.Match(stem);
        if (m.Success
            && DateTime.TryParseExact(
                m.Groups["date"].Value + m.Groups["time"].Value,
                "yyMMddHHmmss",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var at))
            return at.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture);

        return null;
    }

    private static string? Stamp(string date, string time) =>
        DateTime.TryParseExact(
            date + " " + time,
            "yyyy-MM-dd HH:mm:ss",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var at)
            ? at.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture)
            : null;

    private static bool TryDate(string text, string format, out string isoDate)
    {
        if (DateOnly.TryParseExact(text, format, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
        {
            isoDate = d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            return true;
        }

        isoDate = "";
        return false;
    }

    private static IReadOnlyList<RecordingPeriod> SortPeriods(IEnumerable<RecordingPeriod> periods) =>
        periods
            .OrderBy(p => p.StartedAt == null ? 1 : 0)
            .ThenBy(p => p.StartedAt, StringComparer.Ordinal)
            .ThenBy(p => p.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static List<DirectoryInfo> SafeDirectories(DirectoryInfo dir)
    {
        try
        {
            return dir.EnumerateDirectories().ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new List<DirectoryInfo>();
        }
    }

    private static List<FileInfo> SafeFiles(DirectoryInfo dir)
    {
        try
        {
            return dir.EnumerateFiles().ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new List<FileInfo>();
        }
    }
}
