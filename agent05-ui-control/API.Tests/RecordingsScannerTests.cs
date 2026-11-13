using XtractManager.Features.Sessions.Application;
using XtractManager.Features.Sessions.Infrastructure;
using Xunit;

namespace XtractManager.Tests;

/// <summary>The recordings folder read into lessons, periods and tracks. All names here are made up.</summary>
public sealed class RecordingsScannerTests : IDisposable
{
    private const string Meeting = "Maria Lopez's Zoom Meeting";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "XtractManagerTests", "recordings_" + Guid.NewGuid().ToString("N"));

    public RecordingsScannerTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { /* ignore */ }
    }

    private string Put(string relativePath, int bytes = 10)
    {
        var full = Path.Combine(_root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllBytes(full, new byte[bytes]);
        return full;
    }

    private void PutZoomPeriod(string folder, string recordingId, bool video = true)
    {
        Put($"{folder}/audio{recordingId}.m4a", 300);
        Put($"{folder}/Audio Record/audioMariaLopez1{recordingId}.m4a", 120);
        Put($"{folder}/Audio Record/audioОльгаИва2{recordingId}.m4a", 180);
        if (video)
            Put($"{folder}/video{recordingId}.mp4", 50);
        Put($"{folder}/recording.conf", 5);
    }

    [Fact]
    public void Day_folder_with_zoom_periods_gives_mixed_and_participant_tracks()
    {
        var first = $"2026.10.23/2026-10-23 19.01.16 {Meeting}";
        var second = $"2026.10.23/2026-10-23 19.41.16 {Meeting}";
        PutZoomPeriod(second, "1007758391", video: false);
        PutZoomPeriod(first, "1844996676");
        Put($"{first}/chat.txt", 3);
        Put($"{first}/video1844996676.vtt", 3);
        Put($"{first}/double_click_to_convert_01.zoom", 1000);
        Put($"{first}/double_click_to_convert_02.zoom", 24);

        var session = Assert.Single(RecordingsScanner.Scan(_root, f => f.Length / 10.0));

        Assert.Equal("2026.10.23", session.Id);
        Assert.Equal("2026-10-23", session.Date);
        Assert.Null(session.Title);
        Assert.False(session.Loose);
        Assert.Equal(new[] { first, second }, session.Periods.Select(p => p.Id));

        var period = session.Periods[0];
        Assert.Equal("2026-10-23T19:01:16", period.StartedAt);
        Assert.Equal(Meeting, period.Title);
        Assert.Equal(RecordingsScanner.StateReady, period.State);
        Assert.True(period.HasVideo);
        Assert.True(period.HasChat);
        Assert.True(period.HasSubtitles);
        Assert.False(period.HasOldTranscript);
        Assert.Equal(1024, period.RawZoomBytes);

        Assert.Equal(3, period.Tracks.Count);
        var mixed = period.Tracks[0];
        Assert.Equal($"{first}/audio1844996676.m4a", mixed.Id);
        Assert.Equal(RecordingsScanner.KindMixed, mixed.Kind);
        Assert.Null(mixed.Participant);
        Assert.Equal(300, mixed.SizeBytes);
        Assert.Equal(30.0, mixed.DurationSec);

        Assert.Equal(
            new[] { ("Maria Lopez", 120L), ("Ольга Ива", 180L) },
            period.Tracks.Skip(1).Select(t => (t.Participant!, t.SizeBytes)));
        Assert.All(period.Tracks.Skip(1), t => Assert.Equal(RecordingsScanner.KindParticipant, t.Kind));
        Assert.Equal($"{first}/Audio Record/audioMariaLopez11844996676.m4a", period.Tracks[1].Id);

        var later = session.Periods[1];
        Assert.False(later.HasVideo);
        Assert.False(later.HasChat);
        Assert.Equal(0, later.RawZoomBytes);
    }

    [Fact]
    public void Period_that_zoom_did_not_convert_is_shown_without_tracks()
    {
        var raw = $"2026.10.30/2026-10-30 19.01.30 {Meeting}";
        Put($"{raw}/double_click_to_convert_01.zoom", 700);
        Put($"{raw}/recording.conf", 5);
        Directory.CreateDirectory(Path.Combine(_root, "2026.10.30", $"2026-10-30 19.48.00 {Meeting}"));

        var periods = Assert.Single(RecordingsScanner.Scan(_root)).Periods;

        Assert.Equal(new[] { RecordingsScanner.StateUnconverted, RecordingsScanner.StateEmpty }, periods.Select(p => p.State));
        Assert.Equal(700, periods[0].RawZoomBytes);
        Assert.All(periods, p => Assert.Empty(p.Tracks));
    }

    [Fact]
    public void Old_day_folder_with_flat_files_gives_one_mixed_track_per_audio()
    {
        Put("2026.07.17/2026-07-17 20.41.35 .m4a", 40);
        Put("2026.07.17/2026-07-17 20.41.35 .mp4", 400);
        Put("2026.07.17/2026-07-17 20.01.19 .m4a", 30);
        Put("2026.07.17/2026-07-17 20.01.19 .conf", 5);
        Put("2025.08.06/Voice 250806_191245.m4a", 20);
        Put("2025.08.06/Voice_250806_191245_rus.json", 9);
        Put("2025.12.24/2025-12-24 18.13.28.m4a", 25);
        Put("2025.12.24/2025-12-24 18.13.28 transcript.md", 7);
        Put("2025.12.24/notes.png", 7);

        var sessions = RecordingsScanner.Scan(_root);

        Assert.Equal(new[] { "2026.07.17", "2025.12.24", "2025.08.06" }, sessions.Select(s => s.Id));

        var july = sessions[0].Periods;
        Assert.Equal(new[] { "2026-07-17T20:01:19", "2026-07-17T20:41:35" }, july.Select(p => p.StartedAt));
        Assert.Equal(new[] { false, true }, july.Select(p => p.HasVideo));
        var track = Assert.Single(july[1].Tracks);
        Assert.Equal("2026.07.17/2026-07-17 20.41.35 .m4a", track.Id);
        Assert.Equal(july[1].Id, track.Id);
        Assert.Equal(RecordingsScanner.KindMixed, track.Kind);
        Assert.Equal(40, track.SizeBytes);

        var december = Assert.Single(sessions[1].Periods);
        Assert.True(december.HasOldTranscript);

        var august = Assert.Single(sessions[2].Periods);
        Assert.Equal("2025-08-06T19:12:45", august.StartedAt);
        Assert.False(august.HasOldTranscript);
    }

    [Fact]
    public void Periods_left_in_the_root_become_a_loose_lesson_and_other_things_are_skipped()
    {
        PutZoomPeriod($"2026-11-13 19.44.28 {Meeting}", "1111111111");
        PutZoomPeriod($"2026-11-13 19.03.58 {Meeting}", "2222222222");
        PutZoomPeriod($"2026.03.04 - canvases/2026-03-04 19.00.00 {Meeting}", "3333333333");
        Put("2026.03.26 Guest/2026.03.26 Guest.m4a", 15);
        Put("Grammar book.pdf", 9);
        Put("elProyecto/runtime/abc/audio.m4a", 9);
        Put("2026.13.45/2026-13-45 10.00.00.m4a", 9);
        Put("notes 2026.01.01/a.m4a", 9);

        var sessions = RecordingsScanner.Scan(_root);

        Assert.Equal(
            new[] { "@loose/2026-11-13", "2026.03.26 Guest", "2026.03.04 - canvases" },
            sessions.Select(s => s.Id));

        var loose = sessions[0];
        Assert.True(loose.Loose);
        Assert.Equal("2026-11-13", loose.Date);
        Assert.Equal(
            new[] { $"2026-11-13 19.03.58 {Meeting}", $"2026-11-13 19.44.28 {Meeting}" },
            loose.Periods.Select(p => p.Id));
        Assert.Equal(
            $"2026-11-13 19.03.58 {Meeting}/Audio Record/audioMariaLopez12222222222.m4a",
            loose.Periods[0].Tracks[1].Id);

        Assert.Equal("Guest", sessions[1].Title);
        Assert.Null(Assert.Single(sessions[1].Periods).StartedAt);
        Assert.Equal("canvases", sessions[2].Title);
        Assert.Equal("2026-03-04", sessions[2].Date);
    }

    [Theory]
    [InlineData("audioMariaLopez11844996676", "1844996676", "Maria Lopez")]
    [InlineData("audioMariaLopez21844996676", null, "Maria Lopez")]
    [InlineData("audioОльгаИва21007758391", "1007758391", "Ольга Ива")]
    [InlineData("audioGuest7123456789012", "123456789012", "Guest")]
    [InlineData("audioanna1123456789", null, "anna")]
    [InlineData("audio11844996676", "1844996676", "1")]
    [InlineData("voice memo", null, "voice memo")]
    public void Participant_name_is_read_from_the_file_name(string stem, string? recordingId, string expected)
    {
        Assert.Equal(expected, RecordingsScanner.ParticipantName(stem, recordingId));
    }

    [Fact]
    public void Missing_folder_gives_no_lessons()
    {
        Assert.Empty(RecordingsScanner.Scan(Path.Combine(_root, "nope")));
    }

    [Fact]
    public void Scan_leaves_the_folder_as_it_was()
    {
        PutZoomPeriod($"2026.10.23/2026-10-23 19.01.16 {Meeting}", "1844996676");
        var before = Snapshot();

        RecordingsScanner.Scan(_root, _ => 1.0);

        Assert.Equal(before, Snapshot());
    }

    private List<string> Snapshot() =>
        Directory.EnumerateFileSystemEntries(_root, "*", SearchOption.AllDirectories)
            .OrderBy(p => p, StringComparer.Ordinal)
            .Select(p => File.Exists(p)
                ? $"{p}|{new FileInfo(p).Length}|{File.GetLastWriteTimeUtc(p).Ticks}"
                : p)
            .ToList();
}
