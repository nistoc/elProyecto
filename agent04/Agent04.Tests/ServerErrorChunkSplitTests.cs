using System.Net;
using System.Text;
using System.Text.Json;
using Agent04.Application;
using Agent04.Features.Transcription.Application;
using Agent04.Features.Transcription.Domain;
using Agent04.Features.Transcription.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Agent04.Tests;

/// <summary>
/// A chunk the server refused three times (HTTP 5xx) is cut into parts; the parts come back as one answer
/// of the chunk with time marks. The real client talks to a scripted server; only ffmpeg is replaced.
/// </summary>
public sealed class ServerErrorChunkSplitTests : IDisposable
{
    private const string ServerError = """{ "error": { "message": "The server had an error processing your request.", "type": "server_error" } }""";
    private const string PlainAnswer = """{ "text": "texto sin marcas", "usage": { "type": "tokens", "input_tokens": 100, "output_tokens": 20, "total_tokens": 120 } }""";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "a04autosplit_" + Guid.NewGuid().ToString("N")[..8]);

    public ServerErrorChunkSplitTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* ignore */ }
    }

    /// <summary>A request as the server saw it: what the audio file held and the form fields.</summary>
    private sealed record Seen(string Audio, IReadOnlyDictionary<string, string> Fields)
    {
        public bool Timed => Fields.ContainsKey("response_format");
    }

    private sealed class ScriptedServer : HttpMessageHandler
    {
        private readonly Func<Seen, (HttpStatusCode Status, string Body)> _script;
        public List<Seen> Requests { get; } = new();

        public ScriptedServer(Func<Seen, (HttpStatusCode, string)> script) => _script = script;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var fields = new Dictionary<string, string>();
            var audio = "";
            foreach (var part in (MultipartFormDataContent)request.Content!)
            {
                var name = part.Headers.ContentDisposition?.Name?.Trim('"') ?? "";
                var value = await part.ReadAsStringAsync(cancellationToken);
                if (name == "file") audio = value;
                else fields[name] = value;
            }

            var seen = new Seen(audio, fields);
            Requests.Add(seen);
            var (status, body) = _script(seen);
            cancellationToken.ThrowIfCancellationRequested();
            return new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }

    /// <summary>ffmpeg and ffprobe: a chunk of a set length; a cut part is a file that says which part it is.</summary>
    private sealed class FakeAudio : IAudioUtils
    {
        public double DurationSec { get; init; } = 157.0;
        public bool FailToCut { get; init; }
        public List<(double Start, double Duration, string Output)> Cuts { get; } = new();

        public string? WhichOr(string? pathKey, string defaultName) => defaultName;
        public (double DurationSeconds, long SizeBytes) GetDurationAndSize(string ffprobePath, string filePath) => (DurationSec, 1);

        public void ExtractAudioSegmentCopyOrReencode(string ffmpegPath, string inputPath, double startSeconds, double durationSeconds, string outputPath)
        {
            if (FailToCut)
                throw new InvalidOperationException("ffmpeg failed");
            File.WriteAllText(outputPath, "part:" + Cuts.Count);
            Cuts.Add((startSeconds, durationSeconds, outputPath));
        }

        public int CalculateSegmentTime(long sizeBytes, double durationSec, double targetMb) => throw new NotSupportedException();
        public string ReencodeIfNeeded(string ffmpegPath, string inputPath, double targetMb, int bitrateKbps) => throw new NotSupportedException();
        public string FormatMb(long numBytes) => throw new NotSupportedException();
        public string ConvertToWav(string ffmpegPath, string inputPath, string? outputDir = null) => throw new NotSupportedException();
        public void ExtractAudioSegmentCopy(string ffmpegPath, string inputPath, double startSeconds, double durationSeconds, string outputPath) => throw new NotSupportedException();
        public IReadOnlyList<SilenceInterval> DetectSilence(string ffmpegPath, string inputPath, SilenceDetectOptions options, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public SilenceCompressionReport WriteWavWithCompressedSilence(string ffmpegPath, string ffprobePath, string inputPath, string outputWavPath, SilenceDetectOptions detectOptions, double keepSilenceSec, IReadOnlyList<SilenceInterval>? precomputedSilence = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Test";
        public string ApplicationName { get; set; } = "Test";
        public string ContentRootPath { get; set; } = Path.GetTempPath();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    /// <summary>Keeps what the pipeline wrote to its log.</summary>
    private sealed class ListLogger : ILogger<TranscriptionPipeline>
    {
        public List<(LogLevel Level, string Message)> Lines { get; } = new();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Lines.Add((logLevel, formatter(state, exception)));
    }

    private TranscriptionPipeline CreatePipeline(FakeAudio audio, ScriptedServer server, ILogger<TranscriptionPipeline>? logger = null)
    {
        var http = new HttpClient(server) { BaseAddress = new Uri("https://api.example.invalid/") };
        var client = new OpenAITranscriptionClient(http, "test-key", "gpt-4o-transcribe-diarize") { RetryDelayUnit = TimeSpan.Zero };
        var artifacts = new ProjectArtifactService(
            new JobArtifactRootRegistry(),
            new ConfigurationBuilder().Build(),
            new PerJobCancellationManagerFactory(),
            new AudioUtils(),
            new TranscriptionOutputWriter(NullLogger<TranscriptionOutputWriter>.Instance),
            new WorkspaceRoot(Path.GetTempPath()),
            new TestHostEnvironment(),
            NullLogger<ProjectArtifactService>.Instance);
        return new TranscriptionPipeline(audio, new TranscriptionCache(Path.Combine(_root, "cache")), client, new TranscriptionMerger(), artifacts, null, logger);
    }

    private static async Task<TranscriptionConfig> LoadConfigAsync(int? parts = null)
    {
        var config = await TranscriptionConfig.FromFileAsync(Path.Combine(AppContext.BaseDirectory, "config", "default.json"));
        return parts is { } n
            ? config.WithOverrides(new Dictionary<string, object?> { [ServerErrorChunkSplit.PartsConfigKey] = n })
            : config;
    }

    private static TranscriptionClientOptions ChunkOptions(TranscriptionConfig config) => new()
    {
        Model = config.Get<string>("model"),
        Language = config.Language,
        Temperature = config.Temperature,
        ResponseFormat = config.Get<string>("response_format"),
        ChunkingStrategy = config.Get<string>("chunking_strategy"),
        ChunkIndex = 3,
    };

    private string NewChunk(string content = "chunk:3", string name = "lesson_part_003.wav")
    {
        var path = Path.Combine(_root, name);
        File.WriteAllText(path, content);
        return path;
    }

    /// <summary>
    /// A timed answer: one segment at 0.4 s (inside the 1 s a part shares with the previous one) and one at 5.0 s.
    /// </summary>
    private static string TimedAnswer(string tag) => $$"""
        {
          "task": "transcribe", "duration": 40.0, "text": "dup {{tag}} line {{tag}}",
          "segments": [
            { "type": "transcript.text.segment", "id": "seg_0", "start": 0.4, "end": 0.9, "text": "dup {{tag}}", "speaker": "A" },
            { "type": "transcript.text.segment", "id": "seg_1", "start": 5.0, "end": 6.5, "text": "line {{tag}}", "speaker": "A" }
          ],
          "usage": { "type": "tokens", "input_tokens": 100, "output_tokens": 50, "total_tokens": 150,
                     "input_token_details": { "audio_tokens": 90, "text_tokens": 10 } }
        }
        """;

    /// <summary>The server of the real case: refuses the whole chunk with time marks, takes every part.</summary>
    private static (HttpStatusCode, string) RefuseTheWholeChunk(Seen seen)
    {
        if (seen.Audio.StartsWith("chunk:", StringComparison.Ordinal))
            return seen.Timed ? (HttpStatusCode.InternalServerError, ServerError) : (HttpStatusCode.OK, PlainAnswer);
        return (HttpStatusCode.OK, TimedAnswer(seen.Audio["part:".Length..]));
    }

    private static IReadOnlyDictionary<string, object?> Block(IReadOnlyDictionary<string, object?> raw, string key) =>
        Assert.IsAssignableFrom<IReadOnlyDictionary<string, object?>>(raw[key]);

    [Theory]
    [InlineData(157.0, null, 4)]   // the usual chunk, the shared config
    [InlineData(157.0, 6, 6)]
    [InlineData(157.0, 0, 0)]      // switched off
    [InlineData(157.0, 1, 0)]
    [InlineData(35.0, 4, 3)]       // parts are not made shorter than 10 s
    [InlineData(19.9, 4, 0)]       // too short for two parts
    [InlineData(1000.0, 64, 16)]
    public async Task Parts_for_a_chunk(double chunkDurationSec, int? configured, int expected)
    {
        var config = await LoadConfigAsync(configured);

        Assert.Equal(expected, ServerErrorChunkSplit.PartsFor(config, chunkDurationSec));
    }

    [Fact]
    public async Task A_config_without_the_key_cuts_into_four_parts()
    {
        var config = (await LoadConfigAsync()).WithOverrides(
            new Dictionary<string, object?> { [ServerErrorChunkSplit.PartsConfigKey] = null });

        Assert.Null(config.Get<int?>(ServerErrorChunkSplit.PartsConfigKey));
        Assert.Equal(4, ServerErrorChunkSplit.PartsFor(config, 157.0));
    }

    [Fact]
    public async Task The_shared_config_cuts_a_refused_chunk_into_four_parts()
    {
        Assert.Equal(4, (await LoadConfigAsync()).Get<int?>(ServerErrorChunkSplit.PartsConfigKey));
    }

    [Fact]
    public async Task A_chunk_refused_three_times_comes_back_in_parts_with_times_of_the_chunk()
    {
        var audio = new FakeAudio();
        var server = new ScriptedServer(RefuseTheWholeChunk);
        var config = await LoadConfigAsync();

        var result = await CreatePipeline(audio, server)
            .TranscribeChunkOrItsPartsAsync(config, NewChunk(), ChunkOptions(config), CancellationToken.None);

        // 157 s in 4 parts with 1 s shared: 0–40, 39–79, 78–118, 117–157
        Assert.Equal(new[] { 0.0, 39.0, 78.0, 117.0 }, audio.Cuts.Select(c => Math.Round(c.Start, 6)));
        Assert.All(audio.Cuts, c => Assert.Equal(40.0, c.Duration, 6));

        // each part's 5.0 s stands at its place in the chunk; a part's 0.4 s repeats the previous part and is dropped
        Assert.Equal(new[] { 0.4, 5.0, 44.0, 83.0, 122.0 }, result.Segments.Select(s => s.Start));
        Assert.Equal(new[] { "dup 0", "line 0", "line 1", "line 2", "line 3" }, result.Segments.Select(s => s.Text));
        Assert.All(result.Segments, s => Assert.Equal("A", s.Speaker));
        Assert.Equal(45.5, result.Segments[2].End, 6);
        Assert.Equal("dup 0 line 0 line 1 line 2 line 3", result.RawResponse["text"]);

        // three refusals of the chunk, then one request per part, all asked for time marks
        Assert.Equal(7, server.Requests.Count);
        Assert.Equal(new[] { "chunk:3", "chunk:3", "chunk:3", "part:0", "part:1", "part:2", "part:3" }, server.Requests.Select(r => r.Audio));
        Assert.All(server.Requests, r => Assert.Equal("diarized_json", r.Fields["response_format"]));
        Assert.All(server.Requests, r => Assert.Equal("gpt-4o-transcribe-diarize", r.Fields["model"]));

        var usage = Block(result.RawResponse, "usage");
        Assert.Equal(400.0, usage["input_tokens"]);
        Assert.Equal(200.0, usage["output_tokens"]);
        Assert.Equal(600.0, usage["total_tokens"]);
        Assert.Equal("tokens", usage["type"]);
        Assert.Equal(360.0, Block(usage, "input_token_details")["audio_tokens"]);

        var split = Block(result.RawResponse, ServerErrorChunkSplit.RawBlockKey);
        Assert.Equal("server_error", split["reason"]);
        Assert.Equal(500, split["status"]);
        Assert.Equal(3, split["attempts"]);
        Assert.Equal(0, split["parts_without_time_marks"]);
        var parts = Assert.IsType<object?[]>(split["parts"]);
        Assert.Equal(4, parts.Length);
        var second = Assert.IsAssignableFrom<IReadOnlyDictionary<string, object?>>(parts[1]);
        Assert.Equal(39.0, second["start_sec"]);
        Assert.Equal(1.0, second["emit_guard_sec"]);
        Assert.True((bool)second["timed"]!);

        // the part files are gone
        Assert.All(audio.Cuts, c => Assert.False(Directory.Exists(Path.GetDirectoryName(c.Output))));
    }

    [Fact]
    public async Task A_part_that_came_back_without_time_marks_stands_at_the_start_of_that_part()
    {
        var audio = new FakeAudio();
        var server = new ScriptedServer(seen => seen.Audio == "part:2"
            ? (seen.Timed ? (HttpStatusCode.InternalServerError, ServerError) : (HttpStatusCode.OK, PlainAnswer))
            : RefuseTheWholeChunk(seen));
        var config = await LoadConfigAsync();

        var result = await CreatePipeline(audio, server)
            .TranscribeChunkOrItsPartsAsync(config, NewChunk(), ChunkOptions(config), CancellationToken.None);

        Assert.Equal(new[] { 0.4, 5.0, 44.0, 78.0, 122.0 }, result.Segments.Select(s => s.Start));
        Assert.Equal("texto sin marcas", result.Segments[3].Text);
        Assert.Null(result.Segments[3].Speaker);

        var split = Block(result.RawResponse, ServerErrorChunkSplit.RawBlockKey);
        Assert.Equal(1, split["parts_without_time_marks"]);
        var third = Assert.IsAssignableFrom<IReadOnlyDictionary<string, object?>>(Assert.IsType<object?[]>(split["parts"])[2]);
        Assert.False((bool)third["timed"]!);
        // the part itself was refused three times and then asked without response_format
        Assert.Equal(4, server.Requests.Count(r => r.Audio == "part:2"));
    }

    [Fact]
    public async Task A_chunk_the_server_takes_is_asked_once_and_is_not_cut()
    {
        var audio = new FakeAudio();
        var server = new ScriptedServer(_ => (HttpStatusCode.OK, TimedAnswer("whole")));
        var config = await LoadConfigAsync();

        var result = await CreatePipeline(audio, server)
            .TranscribeChunkOrItsPartsAsync(config, NewChunk(), ChunkOptions(config), CancellationToken.None);

        Assert.Single(server.Requests);
        Assert.Empty(audio.Cuts);
        Assert.Equal(2, result.Segments.Count);
        Assert.False(result.RawResponse.ContainsKey(ServerErrorChunkSplit.RawBlockKey));
    }

    [Fact]
    public async Task With_the_split_switched_off_the_refused_chunk_gets_text_without_time_marks_as_before()
    {
        var audio = new FakeAudio();
        var server = new ScriptedServer(RefuseTheWholeChunk);
        var config = await LoadConfigAsync(parts: 0);

        var result = await CreatePipeline(audio, server)
            .TranscribeChunkOrItsPartsAsync(config, NewChunk(), ChunkOptions(config), CancellationToken.None);

        Assert.Empty(audio.Cuts);
        Assert.Equal(4, server.Requests.Count);
        var only = Assert.Single(result.Segments);
        Assert.Equal("texto sin marcas", only.Text);
        Assert.Equal(0.0, only.Start);
        Assert.False(result.RawResponse.ContainsKey(ServerErrorChunkSplit.RawBlockKey));
    }

    [Fact]
    public async Task A_refused_chunk_too_short_to_cut_gets_text_without_time_marks()
    {
        var audio = new FakeAudio { DurationSec = 15.0 };
        var server = new ScriptedServer(RefuseTheWholeChunk);
        var config = await LoadConfigAsync();

        var result = await CreatePipeline(audio, server)
            .TranscribeChunkOrItsPartsAsync(config, NewChunk(), ChunkOptions(config), CancellationToken.None);

        Assert.Empty(audio.Cuts);
        // three refusals, then the same chunk without response_format — not three refusals more
        Assert.Equal(4, server.Requests.Count);
        Assert.False(server.Requests[3].Timed);
        Assert.Equal("texto sin marcas", Assert.Single(result.Segments).Text);
    }

    [Fact]
    public async Task When_the_parts_cannot_be_cut_the_chunk_gets_text_without_time_marks()
    {
        var audio = new FakeAudio { FailToCut = true };
        var server = new ScriptedServer(RefuseTheWholeChunk);
        var config = await LoadConfigAsync();
        var log = new ListLogger();

        var result = await CreatePipeline(audio, server, log)
            .TranscribeChunkOrItsPartsAsync(config, NewChunk(), ChunkOptions(config), CancellationToken.None);

        Assert.Contains(log.Lines, l => l.Level == LogLevel.Warning && l.Message.Contains("transcription in parts failed", StringComparison.Ordinal));
        Assert.Equal(4, server.Requests.Count);
        Assert.Equal("texto sin marcas", Assert.Single(result.Segments).Text);
    }

    [Fact]
    public async Task Cancel_while_the_parts_are_transcribed_stops_the_work()
    {
        using var cts = new CancellationTokenSource();
        var audio = new FakeAudio();
        var server = new ScriptedServer(seen =>
        {
            if (seen.Audio == "part:1")
                cts.Cancel();
            return RefuseTheWholeChunk(seen);
        });
        var config = await LoadConfigAsync();
        var log = new ListLogger();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => CreatePipeline(audio, server, log)
            .TranscribeChunkOrItsPartsAsync(config, NewChunk(), ChunkOptions(config), cts.Token));

        // a cancel is not a failed split: the chunk is not sent on to the request without time marks
        Assert.DoesNotContain(log.Lines, l => l.Message.Contains("transcription in parts failed", StringComparison.Ordinal));

        // nothing is asked after the cancel: neither the next parts nor the chunk without time marks
        Assert.Equal("part:1", server.Requests[^1].Audio);
        Assert.All(audio.Cuts, c => Assert.False(Directory.Exists(Path.GetDirectoryName(c.Output))));
    }

    /// <summary>
    /// The whole way of a real job: retranscribe of a chunk the server refuses, its answer in the cache and
    /// in <c>chunks_json</c>, and the chunk's markdown on the timeline of the recording.
    /// </summary>
    [Fact]
    public async Task Retranscribe_of_a_refused_chunk_writes_timed_lines_of_its_parts()
    {
        var chunksDir = Path.Combine(_root, "chunks");
        var jsonDir = Path.Combine(_root, "chunks_json");
        Directory.CreateDirectory(chunksDir);
        Directory.CreateDirectory(jsonDir);
        var infos = new[]
        {
            new ChunkInfo(Path.Combine(chunksDir, "lesson_part_000.wav"), 0.0, 0.0),
            new ChunkInfo(Path.Combine(chunksDir, "lesson_part_001.wav"), 157.0, 1.0),
        };
        File.WriteAllText(infos[0].Path, "chunk:0");
        File.WriteAllText(infos[1].Path, "chunk:1");
        await ChunkTimelineFile.SaveAsync(_root, infos, CancellationToken.None);
        await File.WriteAllTextAsync(Path.Combine(jsonDir, "lesson_part_000.json"), TimedAnswer("first"));

        var audio = new FakeAudio();
        var server = new ScriptedServer(RefuseTheWholeChunk);
        var config = await LoadConfigAsync();
        var pipeline = CreatePipeline(audio, server);

        await pipeline.RetranscribeMainChunkAsync(config, _root, "job1", 1, null, CancellationToken.None);

        using (var doc = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(jsonDir, "lesson_part_001.json"))))
        {
            var starts = doc.RootElement.GetProperty("segments").EnumerateArray().Select(s => s.GetProperty("start").GetDouble());
            Assert.Equal(new[] { 0.4, 5.0, 44.0, 83.0, 122.0 }, starts);
            var split = doc.RootElement.GetProperty(ServerErrorChunkSplit.RawBlockKey);
            Assert.Equal(4, split.GetProperty("parts").GetArrayLength());
            Assert.Equal(400, doc.RootElement.GetProperty("usage").GetProperty("input_tokens").GetInt32());
        }

        await pipeline.RebuildCombinedOutputsFromPerChunkJsonAsync(config, _root, "job1", null, CancellationToken.None);

        // chunk 1 starts at 157 s of the recording; its first second repeats chunk 0
        var md = await File.ReadAllTextAsync(Path.Combine(_root, "chunks_md", "lesson_part_001.md"));
        Assert.Contains("- 162.00 speaker_0: \"line 0\"", md);
        Assert.Contains("- 201.00 speaker_0: \"line 1\"", md);
        Assert.Contains("- 240.00 speaker_0: \"line 2\"", md);
        Assert.Contains("- 279.00 speaker_0: \"line 3\"", md);
        Assert.DoesNotContain("dup", md);
    }
}
