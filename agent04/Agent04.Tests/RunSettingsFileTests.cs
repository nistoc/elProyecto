using System.Text.Json;
using Agent04.Features.Transcription.Domain;
using Agent04.Features.Transcription.Infrastructure;
using Xunit;

namespace Agent04.Tests;

/// <summary><c>run_settings.json</c>: the record of what a job started with.</summary>
public sealed class RunSettingsFileTests : IDisposable
{
    private readonly string _jobDir = Path.Combine(Path.GetTempPath(), "agent04_run_settings_" + Guid.NewGuid().ToString("N"));

    public RunSettingsFileTests() => Directory.CreateDirectory(_jobDir);

    public void Dispose()
    {
        try { Directory.Delete(_jobDir, true); } catch { /* ignore */ }
    }

    private static TranscriptionConfig SharedConfig() => new(new Dictionary<string, object?>
    {
        ["model"] = "gpt-4o-transcribe-diarize",
        ["response_format"] = "diarized_json",
        ["chunking_strategy"] = "auto",
        ["silence_compress_enabled"] = true,
        ["silence_keep_sec"] = 1.0,
        ["language"] = "es",
    });

    private JsonElement Saved()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(RunSettingsFile.ResolvePath(_jobDir)));
        return doc.RootElement.Clone();
    }

    [Fact]
    public void Job_without_settings_of_its_own_records_the_shared_config()
    {
        RunSettingsFile.TrySave(_jobDir, JobSettingsFile.Apply(SharedConfig(), _jobDir));

        var saved = Saved();
        Assert.Equal(1, saved.GetProperty("schema_version").GetInt32());
        Assert.Equal("gpt-4o-transcribe-diarize", saved.GetProperty("model").GetString());
        Assert.True(saved.GetProperty("silence_compress_enabled").GetBoolean());
        Assert.Equal(1.0, saved.GetProperty("silence_keep_sec").GetDouble());
        Assert.Equal(0, saved.GetProperty("from_job_settings").GetArrayLength());
    }

    [Fact]
    public void Job_settings_are_recorded_with_the_keys_they_set()
    {
        File.WriteAllText(
            JobSettingsFile.ResolvePath(_jobDir),
            """{ "schema_version": 1, "transcription": { "silence_keep_sec": 0.2, "model": "whisper-1" } }""");

        RunSettingsFile.TrySave(_jobDir, JobSettingsFile.Apply(SharedConfig(), _jobDir));

        var saved = Saved();
        Assert.Equal("whisper-1", saved.GetProperty("model").GetString());
        Assert.True(saved.GetProperty("silence_compress_enabled").GetBoolean());
        Assert.Equal(0.2, saved.GetProperty("silence_keep_sec").GetDouble());
        Assert.Equal(
            new[] { "model", "silence_keep_sec" },
            saved.GetProperty("from_job_settings").EnumerateArray().Select(x => x.GetString()).ToArray());
    }

    [Fact]
    public void Compression_switched_off_for_the_job_is_recorded()
    {
        File.WriteAllText(
            JobSettingsFile.ResolvePath(_jobDir),
            """{ "transcription": { "silence_compress_enabled": false } }""");

        RunSettingsFile.TrySave(_jobDir, JobSettingsFile.Apply(SharedConfig(), _jobDir));

        var saved = Saved();
        Assert.False(saved.GetProperty("silence_compress_enabled").GetBoolean());
        Assert.Equal(
            new[] { "silence_compress_enabled" },
            saved.GetProperty("from_job_settings").EnumerateArray().Select(x => x.GetString()).ToArray());
    }

    [Fact]
    public void Config_that_names_nothing_records_what_the_run_falls_back_to()
    {
        // no model, no silence keys: the run uses the default model, no compression, and 0.2 s as the pause
        var doc = RunSettingsFile.Build(
            new TranscriptionConfig(new Dictionary<string, object?> { ["language"] = "es" }),
            Array.Empty<string>(),
            new DateTimeOffset(2026, 11, 14, 0, 30, 5, TimeSpan.FromHours(1)));

        Assert.Equal("gpt-4o-transcribe-diarize", doc.Model);
        Assert.False(doc.SilenceCompressEnabled);
        Assert.Equal(0.2, doc.SilenceKeepSec);
        Assert.Empty(doc.FromJobSettings);
        // the time is written in UTC
        Assert.Equal("2026-11-13T23:30:05Z", doc.WrittenAt);
    }

    [Fact]
    public void A_new_run_replaces_the_record_of_the_previous_one()
    {
        RunSettingsFile.TrySave(_jobDir, SharedConfig());
        File.WriteAllText(
            JobSettingsFile.ResolvePath(_jobDir),
            """{ "transcription": { "silence_keep_sec": 2.5 } }""");

        RunSettingsFile.TrySave(_jobDir, JobSettingsFile.Apply(SharedConfig(), _jobDir));

        Assert.Equal(2.5, Saved().GetProperty("silence_keep_sec").GetDouble());
        Assert.False(File.Exists(RunSettingsFile.ResolvePath(_jobDir) + ".tmp"));
    }

    [Fact]
    public void A_folder_that_cannot_be_written_does_not_fail_the_job()
    {
        var missing = Path.Combine(_jobDir, "no_such_folder");

        RunSettingsFile.TrySave(missing, SharedConfig());

        Assert.False(File.Exists(RunSettingsFile.ResolvePath(missing)));
    }
}
