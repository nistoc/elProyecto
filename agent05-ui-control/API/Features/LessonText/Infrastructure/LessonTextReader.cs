using System.Text;
using XtractManager.Features.Jobs.Application;
using XtractManager.Features.Jobs.Infrastructure;
using XtractManager.Features.LessonPackages.Infrastructure;
using XtractManager.Features.LessonText.Application;
using XtractManager.Features.Sessions.Application;
using XtractManager.Features.Sessions.Infrastructure;

namespace XtractManager.Features.LessonText.Infrastructure;

/// <summary>
/// The text of one lesson in one answer, with who said each line. Per recording the text is read the way
/// the word count reads it (<see cref="LessonWordIndex"/>): the newest dialogue, else the newest run of
/// each track — the participant tracks when there are any, else the mixed file — and, for a recording no
/// job transcribed, the answer of another recognition service in its day folder. Of a job its raw
/// transcript is read, never the refined one. The speakers:
/// <list type="bullet">
/// <item>a dialogue and participant tracks: the name of the participant;</item>
/// <item>a mixed file the model told speakers in (<c>speaker_0</c>, <c>speaker_1</c>…): as the model told them;</item>
/// <item>a mixed file transcribed by the plain-text model and also by the diarize model: the text of the first,
/// the speakers of the second, tied across its chunks (<see cref="DiarizedSpeakers"/>) and laid onto our lines
/// (<see cref="SpeakerOverlay"/>) — <see cref="TrackTextRuns"/> tells the runs apart;</item>
/// <item>a mixed file transcribed without speakers: those of the other service's answer to the same file,
/// laid onto our lines by time and sound (<see cref="SpeakerOverlay"/>), a line cut where the speaker changes;</item>
/// </list>
/// and the roles: given by a person (<see cref="ILessonSpeakersStore"/>), else guessed (<see cref="LessonRoleGuess"/>).
/// Every file is only read.
/// </summary>
public sealed class LessonTextReader : ILessonTextReader
{
    private readonly IRecordingsCatalog _catalog;
    private readonly IJobStore _store;
    private readonly IJobWorkspace _workspace;
    private readonly ILessonSpeakersStore _speakers;

    public LessonTextReader(IRecordingsCatalog catalog, IJobStore store, IJobWorkspace workspace, ILessonSpeakersStore speakers)
    {
        _catalog = catalog;
        _store = store;
        _workspace = workspace;
        _speakers = speakers;
    }

    private sealed record RawLine(double Start, string? Speaker, string Text);

    private sealed record Read(string Source, IReadOnlyList<string> JobIds, IReadOnlyList<string> Files, string SpeakersFrom, IReadOnlyList<RawLine> Lines)
    {
        /// <summary>Of an overlay: our words, and how many of them were found among the heard ones.</summary>
        public (int Words, int Matched)? Overlay { get; init; }

        public SpeakerTies? Ties { get; init; }
    }

    public async Task<LessonTextDocument?> ReadAsync(string date, CancellationToken ct = default)
    {
        if (!_catalog.Enabled)
            return null;
        var sessions = (await LessonSessions.WithRunsAsync(_catalog, _store, _workspace, ct))
            .Where(s => s.Date == date)
            .ToList();
        if (sessions.Count == 0)
            return null;

        var roles = _speakers.Roles(date);
        var recordings = new List<LessonTextRecording>();
        foreach (var session in sessions)
        {
            var dayAnswers = !session.Loose && _catalog.RootPath is { } root
                ? LessonWordIndex.AnswersOfDay(Path.Combine(root, session.Id))
                : Array.Empty<FileInfo>();
            var audioFiles = session.Periods.Sum(p => p.Tracks.Count(t => t.Kind == RecordingsScanner.KindMixed));
            foreach (var period in session.Periods.OrderBy(p => p.StartedAt ?? p.Id, StringComparer.Ordinal))
            {
                ct.ThrowIfCancellationRequested();
                if (ReadPeriod(period, dayAnswers, audioFiles) is { } read)
                    recordings.Add(Shape(period, read, roles));
            }
        }

        return new LessonTextDocument(date, recordings);
    }

    private Read? ReadPeriod(RecordingPeriod period, IReadOnlyList<FileInfo> dayAnswers, int audioFilesOfDay)
    {
        if (period.Dialogues.Count > 0)
        {
            var dialogue = period.Dialogues.OrderBy(d => d.CreatedAt ?? "", StringComparer.Ordinal).Last().JobId;
            if (LessonWordIndex.TextOf(_workspace.GetJobDirectoryPath(dialogue))?.Raw is { } merged)
            {
                var lines = Entries(merged).Select(e => new RawLine(e.TimeSec, Plain(e.Speaker), e.Body)).ToList();
                return new Read(LessonTextSource.Dialogue, new[] { dialogue }, new[] { merged.Name }, LessonSpeakersFrom.Participants, lines);
            }
        }

        var participants = period.Tracks
            .Where(t => t.Kind == RecordingsScanner.KindParticipant)
            .Select(t => (Track: t, Job: NewestText(t)))
            .Where(x => x.Job != null)
            .ToList();
        if (participants.Count > 0)
        {
            var lines = new List<RawLine>();
            foreach (var (track, job) in participants)
            {
                var name = string.IsNullOrWhiteSpace(track.Participant) ? Path.GetFileNameWithoutExtension(track.FileName) : track.Participant!;
                lines.AddRange(Entries(job!.Value.Raw).Select(e => new RawLine(e.TimeSec, name, e.Body)));
            }

            return new Read(
                LessonTextSource.Tracks,
                participants.Select(p => p.Job!.Value.JobId).ToList(),
                participants.Select(p => p.Job!.Value.Raw.Name).ToList(),
                LessonSpeakersFrom.Participants,
                lines.OrderBy(l => l.Start).ToList());
        }

        var mixed = period.Tracks.FirstOrDefault(t => t.Kind == RecordingsScanner.KindMixed);
        var answerFile = mixed == null ? null : OtherServiceAnswer.For(mixed.FileName, dayAnswers, audioFilesOfDay);
        if (mixed != null && NewestText(mixed) is { } text)
        {
            var entries = Entries(text.Raw);
            if (entries.Select(e => e.Speaker).Distinct(StringComparer.Ordinal).Count() > 1)
            {
                return new Read(
                    LessonTextSource.Tracks, new[] { text.JobId }, new[] { text.Raw.Name }, LessonSpeakersFrom.Model,
                    entries.Select(e => new RawLine(e.TimeSec, Plain(e.Speaker), e.Body)).ToList());
            }

            foreach (var source in TrackTextRuns.SpeakerSources(mixed.Runs, text.Run))
            {
                var directory = _workspace.GetJobDirectoryPath(source.JobId);
                if (JobTimedLinesReader.ReadDiarized(directory) is not { } segments
                    || LessonWordIndex.TextOf(directory)?.Raw is not { } sourceRaw)
                    continue;
                var tied = DiarizedSpeakers.Tie(segments);
                if (tied.Words.Count == 0)
                    continue;
                var (overlaid, ourWords, found) = Overlay(entries, tied.Words);
                return new Read(
                    LessonTextSource.Tracks, new[] { text.JobId, source.JobId }, new[] { text.Raw.Name, sourceRaw.Name },
                    LessonSpeakersFrom.ModelOverlay, overlaid)
                {
                    Overlay = (ourWords, found),
                    Ties = tied.Ties,
                };
            }

            if (answerFile != null && OtherServiceAnswer.TryRead(answerFile) is { } answer)
            {
                var (lines, words, matched) = Overlay(entries, answer.Words);
                return new Read(
                    LessonTextSource.Tracks, new[] { text.JobId }, new[] { text.Raw.Name, answerFile.Name },
                    LessonSpeakersFrom.OtherService, lines)
                {
                    Overlay = (words, matched),
                };
            }

            return new Read(
                LessonTextSource.Tracks, new[] { text.JobId }, new[] { text.Raw.Name }, LessonSpeakersFrom.None,
                entries.Select(e => new RawLine(e.TimeSec, null, e.Body)).ToList());
        }

        if (answerFile != null && OtherServiceAnswer.TryRead(answerFile) is { } only)
        {
            return new Read(
                LessonTextSource.OtherService, Array.Empty<string>(), new[] { answerFile.Name }, LessonSpeakersFrom.OtherService,
                only.Segments.Where(s => s.Text.Length > 0).Select(s => new RawLine(s.Start, s.Speaker, s.Text)).ToList());
        }

        return null;
    }

    /// <summary>The run of the track that gives its text (<see cref="TrackTextRuns.TextOrder"/>) and has a raw transcript.</summary>
    private (string JobId, FileInfo Raw, TrackRun Run)? NewestText(RecordingTrack track)
    {
        foreach (var run in TrackTextRuns.TextOrder(track.Runs))
        {
            if (!run.HasTranscript)
                continue;
            if (LessonWordIndex.TextOf(_workspace.GetJobDirectoryPath(run.JobId))?.Raw is { } raw)
                return (run.JobId, raw, run);
        }

        return null;
    }

    /// <summary>Our lines cut where the speaker of the heard words changes, with our words and how many were found.</summary>
    private static (List<RawLine> Lines, int Words, int Matched) Overlay(IReadOnlyList<TranscriptEntry> entries, IReadOnlyList<HeardWord> heard)
    {
        var lines = new List<RawLine>();
        int words = 0, matched = 0;
        for (var i = 0; i < entries.Count; i++)
        {
            double? next = i + 1 < entries.Count ? entries[i + 1].TimeSec : null;
            foreach (var part in SpeakerOverlay.Split(entries[i].Body, entries[i].TimeSec, next, heard))
            {
                lines.Add(new RawLine(part.Start, part.Speaker, part.Text));
                words += part.Words;
                matched += part.Matched;
            }
        }

        return (lines, words, matched);
    }

    private static IReadOnlyList<TranscriptEntry> Entries(FileInfo file) =>
        TrackDialogueMerger.Parse(File.ReadAllText(file.FullName, Encoding.UTF8))
            .Where(e => !string.IsNullOrWhiteSpace(e.Body))
            .ToList();

    private static string Plain(string speaker) => speaker.Replace(TrackDialogueMerger.UntimedSuffix, "").Trim();

    /// <summary>
    /// The guessed role of a speaker nobody gave one. A role a person gave to another speaker wins over the
    /// guess: when the person made the other one the teacher, this one is the student, and the other way round.
    /// </summary>
    private static string? Guessed(string id, IReadOnlyDictionary<string, string> guessed, IReadOnlyDictionary<string, string> given)
    {
        if (!guessed.TryGetValue(id, out var guess))
            return null;
        var takenByOther = given.Any(g => g.Key != id && g.Value == guess);
        if (!takenByOther)
            return guess;
        var opposite = guess == LessonRole.Teacher ? LessonRole.Student : guess == LessonRole.Student ? LessonRole.Teacher : null;
        return opposite != null && !given.Any(g => g.Key != id && g.Value == opposite) ? opposite : null;
    }

    private static LessonTextRecording Shape(
        RecordingPeriod period, Read read, IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> roles)
    {
        var counts = new Dictionary<string, (int Lines, int Words, int Latin)>(StringComparer.Ordinal);
        foreach (var line in read.Lines.Where(l => l.Speaker != null))
        {
            var (words, latin) = LessonRoleGuess.Count(line.Text);
            counts.TryGetValue(line.Speaker!, out var known);
            counts[line.Speaker!] = (known.Lines + 1, known.Words + words, known.Latin + latin);
        }

        var guessed = LessonRoleGuess.Guess(counts.ToDictionary(c => c.Key, c => c.Value.Words, StringComparer.Ordinal));
        roles.TryGetValue(period.Id, out var ofRecording);
        roles.TryGetValue(ILessonSpeakersStore.AllRecordings, out var ofLesson);

        var ordered = counts.OrderByDescending(c => c.Value.Words).ThenBy(c => c.Key, StringComparer.Ordinal).ToList();
        var givenOf = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (id, _) in ordered)
        {
            if (ofRecording != null && ofRecording.TryGetValue(id, out var forRecording))
                givenOf[id] = forRecording;
            else if (ofLesson != null && ofLesson.TryGetValue(id, out var forLesson))
                givenOf[id] = forLesson;
            else if (LessonRoleGuess.RoleNamed(id) is { } named)
                givenOf[id] = named;
        }

        var speakers = new List<LessonTextSpeaker>();
        var roleOf = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var (id, count) in ordered)
        {
            var given = givenOf.TryGetValue(id, out var byPerson) ? byPerson : null;
            var role = given ?? Guessed(id, guessed, givenOf);
            roleOf[id] = role;
            speakers.Add(new LessonTextSpeaker(
                id, role, given != null, count.Lines, count.Words,
                count.Words == 0 ? 0 : Math.Round((double)count.Latin / count.Words, 3)));
        }

        var lines = read.Lines
            .Select(l => new LessonTextLine(Math.Round(l.Start, 2), l.Speaker, l.Speaker == null ? null : roleOf[l.Speaker], l.Text))
            .ToList();
        return new LessonTextRecording(period.Id, period.StartedAt, read.Source, read.JobIds, read.Files, read.SpeakersFrom, speakers, lines)
        {
            OverlayWords = read.Overlay?.Words,
            OverlayWordsMatched = read.Overlay?.Matched,
            SpeakerTies = read.Ties,
        };
    }
}
