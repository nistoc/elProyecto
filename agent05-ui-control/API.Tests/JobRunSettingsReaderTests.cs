using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using XtractManager.Features.Jobs.Application;
using XtractManager.Features.Jobs.Infrastructure;
using Xunit;

namespace XtractManager.Tests;

/// <summary>What a job was transcribed with, read from its folder.</summary>
public sealed class JobRunSettingsReaderTests : IDisposable
{
    private readonly string _workspace = Path.Combine(Path.GetTempPath(), "XtractManagerTests", "run_settings_" + Guid.NewGuid().ToString("N"));
    private readonly string _job;

    public JobRunSettingsReaderTests()
    {
        _job = Path.Combine(_workspace, "job000000001");
        Directory.CreateDirectory(_job);
    }

    public void Dispose()
    {
        try { Directory.Delete(_workspace, true); } catch { /* ignore */ }
    }

    private string Put(string relativePath, string content)
    {
        var full = Path.Combine(_job, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content, new UTF8Encoding(false));
        return full;
    }

    private JobRunSettings? Read() => JobRunSettingsReader.Read(_job);

    [Fact]
    public void Folder_that_tells_nothing_gives_nothing()
    {
        Assert.Null(Read());
        Assert.Null(JobRunSettingsReader.Read(Path.Combine(_workspace, "no_such_job")));
        Assert.Null(JobRunSettingsReader.Read(""));
    }

    [Fact]
    public void Record_of_the_transcriber_is_read_as_it_is()
    {
        Put("run_settings.json", """
            { "schema_version": 1, "written_at": "2026-11-14T00:10:00Z", "model": "gpt-4o-transcribe-diarize",
              "silence_compress_enabled": true, "silence_keep_sec": 1.0, "from_job_settings": ["silence_keep_sec"] }
            """);

        var settings = Read();

        Assert.NotNull(settings);
        Assert.Equal(JobRunSettings.SourceRun, settings.Source);
        Assert.Equal("gpt-4o-transcribe-diarize", settings.Model);
        Assert.True(settings.SilenceCompress);
        Assert.Equal(1.0, settings.SilenceKeepSec);
        Assert.Equal(new[] { "silence_keep_sec" }, settings.FromJobSettings);
    }

    [Fact]
    public void Pause_is_not_shown_for_a_run_without_compression()
    {
        // the transcriber records the pause of the config even when nothing is cut out
        Put("run_settings.json", """
            { "model": "whisper-1", "silence_compress_enabled": false, "silence_keep_sec": 0.2, "from_job_settings": ["model", "silence_compress_enabled"] }
            """);

        var settings = Read();

        Assert.NotNull(settings);
        Assert.Equal("whisper-1", settings.Model);
        Assert.False(settings.SilenceCompress);
        Assert.Null(settings.SilenceKeepSec);
        Assert.Equal(new[] { "model", "silence_compress_enabled" }, settings.FromJobSettings);
    }

    [Fact]
    public void Record_of_the_transcriber_wins_over_what_lies_in_the_folder()
    {
        Put("run_settings.json", """{ "model": "gpt-4o-transcribe-diarize", "silence_compress_enabled": true, "silence_keep_sec": 1.0 }""");
        Put("timeline/speech_map.json", """{ "schema_version": 1, "keep_silence_sec": 0.2, "pieces": [] }""");
        Put("job_settings.json", """{ "transcription": { "model": "whisper-1" } }""");

        var settings = Read();

        Assert.NotNull(settings);
        Assert.Equal(JobRunSettings.SourceRun, settings.Source);
        Assert.Equal("gpt-4o-transcribe-diarize", settings.Model);
        Assert.Equal(1.0, settings.SilenceKeepSec);
        Assert.Empty(settings.FromJobSettings);
    }

    [Fact]
    public void Old_job_with_a_speech_map_was_compressed_with_the_pause_of_the_map()
    {
        Put("timeline/speech_map.json", """{ "schema_version": 1, "source_duration_sec": 1251.4, "output_duration_sec": 614.2, "keep_silence_sec": 1, "pieces": [] }""");
        // the report of an earlier run with another pause: the map is what the transcript was made with
        Put("converted_wav/lesson_silc.silence_report.json", """{ "KeepSilenceSec": 0.2 }""");

        var settings = Read();

        Assert.NotNull(settings);
        Assert.Equal(JobRunSettings.SourceArtifacts, settings.Source);
        Assert.True(settings.SilenceCompress);
        Assert.Equal(1.0, settings.SilenceKeepSec);
        Assert.Null(settings.Model);
        Assert.Empty(settings.FromJobSettings);
    }

    [Fact]
    public void Old_job_with_a_silence_report_was_compressed_with_the_pause_of_the_newest_report()
    {
        var older = Put("converted_wav/first_silc.silence_report.json", """{ "InputDurationSec": 1013.6, "KeepSilenceSec": 0.5 }""");
        var newer = Put("converted_wav/second_silc.silence_report.json", """{ "InputDurationSec": 1013.6, "KeepSilenceSec": 0.2 }""");
        File.SetLastWriteTimeUtc(older, new DateTime(2026, 4, 15, 10, 0, 0, DateTimeKind.Utc));
        File.SetLastWriteTimeUtc(newer, new DateTime(2026, 4, 16, 10, 0, 0, DateTimeKind.Utc));

        var settings = Read();

        Assert.NotNull(settings);
        Assert.Equal(JobRunSettings.SourceArtifacts, settings.Source);
        Assert.True(settings.SilenceCompress);
        Assert.Equal(0.2, settings.SilenceKeepSec);
    }

    [Fact]
    public void Old_job_where_silence_was_only_looked_for_ran_without_compression()
    {
        Put("converted_wav/lesson.silence_detect.json", """{ "noiseDb": -40, "minDurationSec": 0.3, "regionCount": 290 }""");

        var settings = Read();

        Assert.NotNull(settings);
        Assert.Equal(JobRunSettings.SourceArtifacts, settings.Source);
        Assert.False(settings.SilenceCompress);
        Assert.Null(settings.SilenceKeepSec);
        Assert.Null(settings.Model);
    }

    [Fact]
    public void Old_job_takes_the_model_the_operator_set_for_it()
    {
        Put("job_settings.json", """{ "schema_version": 1, "transcription": { "model": "whisper-1", "silence_keep_sec": 1.0 } }""");
        Put("timeline/speech_map.json", """{ "keep_silence_sec": 1.0 }""");

        var settings = Read();

        Assert.NotNull(settings);
        Assert.Equal(JobRunSettings.SourceArtifacts, settings.Source);
        Assert.Equal("whisper-1", settings.Model);
        Assert.True(settings.SilenceCompress);
        Assert.Equal(1.0, settings.SilenceKeepSec);
        Assert.Equal(new[] { "model", "silence_keep_sec" }, settings.FromJobSettings);
    }

    [Fact]
    public void What_the_run_left_is_stronger_than_what_the_operator_asked_for()
    {
        // settings changed by hand after the run: the folder still holds a compressed run with 0.2 s
        Put("job_settings.json", """{ "transcription": { "silence_compress_enabled": false, "silence_keep_sec": 3.0 } }""");
        Put("converted_wav/lesson_silc.silence_report.json", """{ "KeepSilenceSec": 0.2 }""");

        var settings = Read();

        Assert.NotNull(settings);
        Assert.True(settings.SilenceCompress);
        Assert.Equal(0.2, settings.SilenceKeepSec);
    }

    [Fact]
    public void Job_that_has_not_run_yet_shows_what_the_operator_set()
    {
        Put("job_settings.json", """{ "schema_version": 1, "transcription": { "silence_keep_sec": 2.5 } }""");

        var settings = Read();

        Assert.NotNull(settings);
        Assert.Equal(JobRunSettings.SourceJobSettings, settings.Source);
        Assert.Null(settings.Model);
        // compression itself was not set for the job: it comes from the shared config, which the folder does not hold
        Assert.Null(settings.SilenceCompress);
        Assert.Equal(2.5, settings.SilenceKeepSec);
        Assert.Equal(new[] { "silence_keep_sec" }, settings.FromJobSettings);
    }

    [Fact]
    public void Job_that_has_not_run_yet_with_compression_set_shows_its_pause()
    {
        Put("job_settings.json", """{ "transcription": { "silence_compress_enabled": true, "silence_keep_sec": 2.5 } }""");

        var settings = Read();

        Assert.NotNull(settings);
        Assert.Equal(JobRunSettings.SourceJobSettings, settings.Source);
        Assert.True(settings.SilenceCompress);
        Assert.Equal(2.5, settings.SilenceKeepSec);
    }

    [Fact]
    public void Job_that_has_not_run_yet_with_compression_switched_off_shows_no_pause()
    {
        Put("job_settings.json", """{ "transcription": { "silence_compress_enabled": false, "silence_keep_sec": 2.5 } }""");

        var settings = Read();

        Assert.NotNull(settings);
        Assert.False(settings.SilenceCompress);
        Assert.Null(settings.SilenceKeepSec);
    }

    [Fact]
    public void Job_of_the_first_generation_without_a_trace_gives_nothing()
    {
        Put("transcript.md", "- 1.00 speaker_0: \"hola\"");
        Put("chunks_json/lesson_part_000.json", "{}");
        Put("converted_wav/lesson.wav", "RIFF");

        Assert.Null(Read());
    }

    [Fact]
    public void Record_that_cannot_be_read_falls_back_to_what_the_run_left()
    {
        Put("run_settings.json", "{ not json");
        Put("timeline/speech_map.json", """{ "keep_silence_sec": 0.2 }""");

        var settings = Read();

        Assert.NotNull(settings);
        Assert.Equal(JobRunSettings.SourceArtifacts, settings.Source);
        Assert.Equal(0.2, settings.SilenceKeepSec);
    }

    [Fact]
    public async Task Job_read_from_the_store_carries_its_run_settings()
    {
        Put("lesson.m4a", "audio");
        Put("run_settings.json", """{ "model": "gpt-4o-transcribe-diarize", "silence_compress_enabled": true, "silence_keep_sec": 1.0, "from_job_settings": [] }""");
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Jobs:WorkspacePath"] = _workspace })
            .Build();
        var workspace = new JobWorkspace(config, new FakeHostEnvironment(), NullLogger<JobWorkspace>.Instance);
        var store = new WorkspaceAwareJobStore(new InMemoryJobStore(), workspace, NullLogger<WorkspaceAwareJobStore>.Instance);

        var snapshot = await store.GetAsync("job000000001");

        Assert.NotNull(snapshot);
        Assert.NotNull(snapshot.RunSettings);
        Assert.Equal(JobRunSettings.SourceRun, snapshot.RunSettings.Source);
        Assert.Equal(1.0, snapshot.RunSettings.SilenceKeepSec);
        // a copy of the snapshot keeps them
        Assert.Same(snapshot.RunSettings, snapshot.Clone().RunSettings);
    }
}
