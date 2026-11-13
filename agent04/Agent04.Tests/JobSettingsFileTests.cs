using Agent04.Features.Transcription.Domain;
using Agent04.Features.Transcription.Infrastructure;
using Xunit;

namespace Agent04.Tests;

public sealed class JobSettingsFileTests : IDisposable
{
    private readonly string _jobDir = Path.Combine(Path.GetTempPath(), "agent04_job_settings_" + Guid.NewGuid().ToString("N"));

    public JobSettingsFileTests() => Directory.CreateDirectory(_jobDir);

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
        ["silence_keep_sec"] = 0.2,
        ["language"] = "es",
    });

    private void WriteSettings(string json) => File.WriteAllText(JobSettingsFile.ResolvePath(_jobDir), json);

    [Fact]
    public void Apply_without_the_file_returns_the_shared_config()
    {
        var shared = SharedConfig();

        Assert.Same(shared, JobSettingsFile.Apply(shared, _jobDir));
    }

    [Fact]
    public void Apply_puts_the_job_settings_on_top_of_the_shared_config()
    {
        WriteSettings("""{ "schema_version": 1, "transcription": { "silence_keep_sec": 1.0, "silence_compress_enabled": false } }""");
        var shared = SharedConfig();

        var config = JobSettingsFile.Apply(shared, _jobDir);

        Assert.Equal(1.0, config.Get<double?>("silence_keep_sec"));
        Assert.False(config.Get<bool?>("silence_compress_enabled"));
        // the rest stays as in the shared config, and the shared config itself is not touched
        Assert.Equal("es", config.Language);
        Assert.Equal("diarized_json", config.Get<string>("response_format"));
        Assert.Equal(0.2, shared.Get<double?>("silence_keep_sec"));
        Assert.True(shared.Get<bool?>("silence_compress_enabled"));
    }

    [Fact]
    public void Apply_gives_another_model_its_own_answer_format()
    {
        WriteSettings("""{ "transcription": { "model": "whisper-1" } }""");

        var config = JobSettingsFile.Apply(SharedConfig(), _jobDir);

        Assert.Equal("whisper-1", config.Get<string>("model"));
        Assert.Equal("verbose_json", config.Get<string>("response_format"));
        Assert.Null(config.Get<string>("chunking_strategy"));
    }

    [Fact]
    public void Apply_keeps_the_shared_answer_format_when_the_job_names_the_shared_model()
    {
        WriteSettings("""{ "transcription": { "model": "GPT-4o-transcribe-diarize" } }""");
        var shared = new TranscriptionConfig(new Dictionary<string, object?>
        {
            ["model"] = "gpt-4o-transcribe-diarize",
            ["response_format"] = "json",
            ["chunking_strategy"] = "none",
        });

        var config = JobSettingsFile.Apply(shared, _jobDir);

        Assert.Equal("json", config.Get<string>("response_format"));
        Assert.Equal("none", config.Get<string>("chunking_strategy"));
    }

    [Fact]
    public void Apply_without_a_transcription_section_changes_nothing()
    {
        WriteSettings("""{ "schema_version": 1 }""");
        var shared = SharedConfig();

        Assert.Same(shared, JobSettingsFile.Apply(shared, _jobDir));
    }

    [Theory]
    [InlineData("not json", "not valid JSON")]
    [InlineData("[]", "root must be an object")]
    [InlineData("""{ "transcription": 5 }""", "'transcription' must be an object")]
    [InlineData("""{ "transcription": { "target_chunk_mb": 1 } }""", "unknown setting 'target_chunk_mb'")]
    [InlineData("""{ "transcription": { "openai_api_key": "x" } }""", "unknown setting 'openai_api_key'")]
    [InlineData("""{ "transcription": { "silence_keep_sec": 0.01 } }""", "'silence_keep_sec' must be a number from 0.05 to 5")]
    [InlineData("""{ "transcription": { "silence_keep_sec": 30 } }""", "'silence_keep_sec' must be a number")]
    [InlineData("""{ "transcription": { "silence_keep_sec": "1.0" } }""", "'silence_keep_sec' must be a number")]
    [InlineData("""{ "transcription": { "silence_compress_enabled": "yes" } }""", "'silence_compress_enabled' must be true or false")]
    [InlineData("""{ "transcription": { "model": "" } }""", "'model' must be a model id")]
    [InlineData("""{ "transcription": { "model": "whisper 1; rm" } }""", "'model' must be a model id")]
    public void Apply_refuses_a_file_it_cannot_take_as_settings(string json, string expectedReason)
    {
        // A job must not silently run with other settings than the operator asked for.
        WriteSettings(json);

        var ex = Assert.Throws<InvalidDataException>(() => JobSettingsFile.Apply(SharedConfig(), _jobDir));

        Assert.Contains(expectedReason, ex.Message);
        Assert.StartsWith(JobSettingsFile.FileName, ex.Message);
    }

    [Theory]
    [InlineData("gpt-4o-transcribe-diarize", "diarized_json", "auto")]
    [InlineData("whisper-1", "verbose_json", null)]
    [InlineData("gpt-4o-transcribe", "json", null)]
    [InlineData("gpt-4o-mini-transcribe", "json", null)]
    public void FormatFor_names_the_answer_format_of_a_model(string model, string format, string? chunking)
    {
        Assert.Equal((format, chunking), JobSettingsFile.FormatFor(model));
    }

    [Fact]
    public void WithOverrides_removes_a_key_given_as_null()
    {
        var config = SharedConfig().WithOverrides(new Dictionary<string, object?> { ["language"] = null, ["model"] = "x" });

        Assert.Null(config.Language);
        Assert.Equal("x", config.Model);
    }
}
