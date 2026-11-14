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
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Agent04.Tests;

/// <summary>
/// A chunk for a model that answers plain text: asked whole and in pieces cut at its pauses, the whole text
/// laid onto the pieces. The real client talks to a scripted server; only ffmpeg is replaced.
/// </summary>
public sealed class SpeechPiecesTranscriptionTests : IDisposable
{
    private const string Model = "gpt-transcribe";
    private const string ServerError = """{ "error": { "message": "The server had an error processing your request.", "type": "server_error" } }""";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "a04pieces_" + Guid.NewGuid().ToString("N")[..8]);

    public SpeechPiecesTranscriptionTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* ignore */ }
    }

    /// <summary>A request as the server saw it: what the audio file held and the form fields, in order.</summary>
    private sealed record Seen(string Audio, IReadOnlyList<(string Name, string Value)> Fields)
    {
        public IReadOnlyList<string> Values(string name) => Fields.Where(f => f.Name == name).Select(f => f.Value).ToList();
        public bool Whole => Audio.StartsWith("chunk", StringComparison.Ordinal);
        public bool Context => Audio.StartsWith("context:", StringComparison.Ordinal);
        public int Piece => int.Parse(Audio[(Audio.IndexOf(':') + 1)..]);
    }

    private sealed class ScriptedServer : HttpMessageHandler
    {
        private readonly Func<Seen, (HttpStatusCode Status, string Body)> _script;
        private readonly List<Seen> _requests = new();

        public ScriptedServer(Func<Seen, (HttpStatusCode, string)> script) => _script = script;

        public IReadOnlyList<Seen> Requests
        {
            get { lock (_requests) return _requests.ToList(); }
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var fields = new List<(string, string)>();
            var audio = "";
            foreach (var part in (MultipartFormDataContent)request.Content!)
            {
                var name = part.Headers.ContentDisposition?.Name?.Trim('"') ?? "";
                var value = await part.ReadAsStringAsync(cancellationToken);
                if (name == "file") audio = value;
                else fields.Add((name, value));
            }

            var seen = new Seen(audio, fields);
            lock (_requests) _requests.Add(seen);
            var (status, body) = _script(seen);
            return new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }

    /// <summary>ffmpeg and ffprobe: a chunk of a set length with set silence; a cut piece is a file that says which piece it is.</summary>
    private sealed class FakeAudio : IAudioUtils
    {
        public double DurationSec { get; init; } = 60.0;
        public IReadOnlyList<SilenceInterval> Silence { get; init; } = Array.Empty<SilenceInterval>();
        public List<SilenceDetectOptions> Searches { get; } = new();
        private readonly List<(int Piece, double Start, double Duration)> _cuts = new();

        private readonly List<(int Piece, double Start, double Duration)> _contextCuts = new();

        public IReadOnlyList<(int Piece, double Start, double Duration)> Cuts
        {
            get { lock (_cuts) return _cuts.OrderBy(c => c.Piece).ToList(); }
        }

        /// <summary>Pieces cut once more with audio around them: which piece, from where, how long.</summary>
        public IReadOnlyList<(int Piece, double Start, double Duration)> ContextCuts
        {
            get { lock (_cuts) return _contextCuts.OrderBy(c => c.Piece).ToList(); }
        }

        public string? WhichOr(string? pathKey, string defaultName) => defaultName;
        public (double DurationSeconds, long SizeBytes) GetDurationAndSize(string ffprobePath, string filePath) => (DurationSec, 1);

        public IReadOnlyList<SilenceInterval> DetectSilence(string ffmpegPath, string inputPath, SilenceDetectOptions options, CancellationToken cancellationToken = default)
        {
            lock (Searches) Searches.Add(options);
            return Silence;
        }

        public void ExtractAudioSegmentCopyOrReencode(string ffmpegPath, string inputPath, double startSeconds, double durationSeconds, string outputPath)
        {
            var name = Path.GetFileNameWithoutExtension(outputPath);
            var kind = name[..name.IndexOf('_')]; // piece | context
            var piece = int.Parse(name[(kind.Length + 1)..]);
            File.WriteAllText(outputPath, kind + ":" + piece);
            lock (_cuts) (kind == "context" ? _contextCuts : _cuts).Add((piece, Math.Round(startSeconds, 3), Math.Round(durationSeconds, 3)));
        }

        public int CalculateSegmentTime(long sizeBytes, double durationSec, double targetMb) => throw new NotSupportedException();
        public string ReencodeIfNeeded(string ffmpegPath, string inputPath, double targetMb, int bitrateKbps) => throw new NotSupportedException();
        public string FormatMb(long numBytes) => throw new NotSupportedException();
        public string ConvertToWav(string ffmpegPath, string inputPath, string? outputDir = null) => throw new NotSupportedException();
        public void ExtractAudioSegmentCopy(string ffmpegPath, string inputPath, double startSeconds, double durationSeconds, string outputPath) => throw new NotSupportedException();
        public SilenceCompressionReport WriteWavWithCompressedSilence(string ffmpegPath, string ffprobePath, string inputPath, string outputWavPath, SilenceDetectOptions detectOptions, double keepSilenceSec, IReadOnlyList<SilenceInterval>? precomputedSilence = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Test";
        public string ApplicationName { get; set; } = "Test";
        public string ContentRootPath { get; set; } = Path.GetTempPath();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private static SilenceInterval Quiet(double from, double to) => new(from, to, to - from);

    /// <summary>Speech at 2–8 s, 9–20 s (with a breath at 14 s) and 21.5–30 s of a 60 s chunk.</summary>
    private static FakeAudio ThreePieces() => new()
    {
        Silence = new[] { Quiet(0, 2), Quiet(8, 9), Quiet(14, 14.4), Quiet(20, 21.5), Quiet(30, 60) },
    };

    private static string Answer(string text, double seconds) =>
        JsonSerializer.Serialize(new { text, usage = new { type = "duration", seconds } });

    private static OpenAITranscriptionClient CreateClient(ScriptedServer server)
    {
        var http = new HttpClient(server) { BaseAddress = new Uri("https://api.example.invalid/") };
        return new OpenAITranscriptionClient(http, "test-key", "gpt-4o-transcribe-diarize") { RetryDelayUnit = TimeSpan.Zero };
    }

    private static SpeechPiecesTranscription CreateWay(FakeAudio audio, ScriptedServer server) =>
        new(audio, CreateClient(server)) { RetryDelayUnit = TimeSpan.Zero };

    /// <summary>The shared config with the job's own model on top, as <c>job_settings.json</c> puts it.</summary>
    private static async Task<TranscriptionConfig> LoadConfigAsync(IReadOnlyDictionary<string, object?>? more = null)
    {
        var config = await TranscriptionConfig.FromFileAsync(Path.Combine(AppContext.BaseDirectory, "config", "default.json"));
        var (format, chunking) = JobSettingsFile.FormatFor(Model);
        var overrides = new Dictionary<string, object?>
        {
            ["model"] = Model,
            ["response_format"] = format,
            ["chunking_strategy"] = chunking,
        };
        foreach (var kv in more ?? new Dictionary<string, object?>())
            overrides[kv.Key] = kv.Value;
        return config.WithOverrides(overrides);
    }

    private static TranscriptionClientOptions ChunkOptions(TranscriptionConfig config) => new()
    {
        Model = config.Get<string>("model"),
        Language = config.Language,
        Prompt = config.Get<string>("prompt"),
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

    private static IReadOnlyDictionary<string, object?> Block(IReadOnlyDictionary<string, object?> raw) =>
        Assert.IsAssignableFrom<IReadOnlyDictionary<string, object?>>(raw[SpeechPiecesTranscription.RawBlockKey]);

    // ---- which model goes this way ----

    [Theory]
    [InlineData("gpt-transcribe", true)]
    [InlineData(" GPT-Transcribe ", true)]
    [InlineData("gpt-transcribe-2026-10-01", true)]
    [InlineData("gpt-4o-transcribe-diarize", false)]
    [InlineData("gpt-4o-transcribe", false)]
    [InlineData("whisper-1", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void The_way_is_for_the_plain_text_model(string? model, bool expected) =>
        Assert.Equal(expected, SpeechPiecesTranscription.AppliesTo(model));

    [Fact]
    public void A_job_that_names_the_plain_text_model_asks_for_json_without_chunking() =>
        Assert.Equal(("json", (string?)null), JobSettingsFile.FormatFor(Model));

    // ---- pieces of a chunk ----

    [Fact]
    public void A_real_pause_ends_a_piece_and_a_breath_does_not()
    {
        var pieces = SpeechPiecePlanner.Plan(ThreePieces().Silence, 60, pauseSec: 1.0, maxPieceSec: 15, minSpeechSec: 0.2);

        Assert.Equal(new[] { (2.0, 8.0), (9.0, 20.0), (21.5, 30.0) }, pieces.Select(p => (p.StartSec, p.EndSec)));
        Assert.Equal(10.6, pieces[1].SpeechSec, 3); // 11 s without the breath of 0.4 s
    }

    [Fact]
    public void A_piece_that_would_grow_too_long_ends_at_a_breath()
    {
        // speech 0–40 s with breaths of 0.4 s every 10 s
        var silence = new[] { Quiet(10, 10.4), Quiet(20, 20.4), Quiet(30, 30.4) };

        var pieces = SpeechPiecePlanner.Plan(silence, 40, pauseSec: 1.0, maxPieceSec: 15, minSpeechSec: 0.2);

        Assert.Equal(new[] { (0.0, 10.0), (10.4, 20.0), (20.4, 30.0), (30.4, 40.0) }, pieces.Select(p => (p.StartSec, p.EndSec)));
    }

    [Fact]
    public void A_kept_pause_measured_a_little_short_is_still_a_pause()
    {
        var silence = new[] { Quiet(5, 5.995), Quiet(10, 10.9) };

        var pieces = SpeechPiecePlanner.Plan(silence, 15, pauseSec: 1.0, maxPieceSec: 15, minSpeechSec: 0.2);

        // 0.995 s ends the piece, 0.9 s does not
        Assert.Equal(new[] { (0.0, 5.0), (5.995, 15.0) }, pieces.Select(p => (p.StartSec, p.EndSec)));
    }

    [Fact]
    public void A_click_between_two_pauses_is_not_a_piece_and_silence_alone_gives_none()
    {
        var click = SpeechPiecePlanner.Plan(new[] { Quiet(0, 3), Quiet(3.1, 6), Quiet(9, 12) }, 12, 1.0, 15, 0.2);
        Assert.Equal(new[] { (6.0, 9.0) }, click.Select(p => (p.StartSec, p.EndSec)));

        Assert.Empty(SpeechPiecePlanner.Plan(new[] { Quiet(0, 12) }, 12, 1.0, 15, 0.2));
        Assert.Single(SpeechPiecePlanner.Plan(Array.Empty<SilenceInterval>(), 12, 1.0, 15, 0.2));
    }

    // ---- the whole text onto the pieces ----

    [Theory]
    [InlineData("beberse", "беберсе")]
    [InlineData("hecho", "эчо")]
    [InlineData("zapato", "сапато")]
    [InlineData("¿Qué?", "ке")]
    [InlineData("gracias", "грасиас")]
    [InlineData("vamos", "бамос")]
    public void A_word_sounds_the_same_in_both_scripts(string latin, string cyrillic) =>
        Assert.Equal(SpeechPieceWordAlignment.Normalize(latin), SpeechPieceWordAlignment.Normalize(cyrillic));

    [Fact]
    public void Words_of_the_whole_text_go_to_the_piece_that_heard_them()
    {
        var placed = SpeechPieceWordAlignment.Assign(
            "Hoy vamos a repetir el gerundio. Да, давайте начнём с глагола hablar. Estoy hablando, estás hablando — вот так.",
            new[]
            {
                "Ой вамос а репетир эль херундио",
                "Да давайте начнем с глагола hablar",
                "Estoy ablando estas hablando вот так",
            });

        Assert.Equal(
            new[]
            {
                "Hoy vamos a repetir el gerundio.",
                "Да, давайте начнём с глагола hablar.",
                "Estoy hablando, estás hablando — вот так.",
            },
            placed.Texts);
        Assert.Equal(18, placed.WordsWhole); // the dash stays with the word before it
        Assert.Equal(18, placed.WordsPieces);
        Assert.Equal(18, placed.Matched);
    }

    [Fact]
    public void A_word_without_a_partner_stays_with_the_word_before_it()
    {
        var placed = SpeechPieceWordAlignment.Assign(
            "Bueno, uno dos tres cuatro cinco seis",
            new string?[] { "uno dos", null, "cinco seis" });

        // "Bueno," has no word before it and goes forward; "tres cuatro" were heard by no piece and stay behind "dos"
        Assert.Equal(new[] { "Bueno, uno dos tres cuatro", "", "cinco seis" }, placed.Texts);
        Assert.Equal(4, placed.Matched);
    }

    [Fact]
    public void Without_words_in_the_pieces_nothing_is_placed()
    {
        var placed = SpeechPieceWordAlignment.Assign("uno dos", new string?[] { null, "" });

        Assert.Equal(new[] { "", "" }, placed.Texts);
        Assert.Equal(0, placed.WordsPieces);
    }

    // ---- a chunk ----

    [Fact]
    public async Task A_chunk_is_asked_whole_and_in_pieces_and_comes_back_as_lines_with_times()
    {
        var audio = ThreePieces();
        var server = new ScriptedServer(seen => (HttpStatusCode.OK, seen.Whole
            ? Answer("Hoy vamos a repetir el gerundio. Да, давайте начнём с глагола hablar. Estoy hablando, estás hablando — вот так.", 60)
            : Answer(new[] { "Ой вамос а репетир эль херундио", "Да давайте начнем с глагола hablar", "Estoy ablando estas hablando вот так" }[seen.Piece], 7)));
        var config = await LoadConfigAsync();
        var activity = new List<string>();

        var answer = await CreateWay(audio, server).TranscribeAsync(NewChunk(), 0, config, ChunkOptions(config), activity.Add);

        Assert.Equal(
            new[]
            {
                new ASRSegment(2.0, 8.0, "Hoy vamos a repetir el gerundio."),
                new ASRSegment(9.0, 20.0, "Да, давайте начнём с глагола hablar."),
                new ASRSegment(21.5, 30.0, "Estoy hablando, estás hablando — вот так."),
            },
            answer.Segments);
        Assert.StartsWith("Hoy vamos a repetir", (string)answer.RawResponse["text"]!);

        // a piece is cut with a little audio around it
        Assert.Equal(new[] { (0, 1.8, 6.45), (1, 8.8, 11.45), (2, 21.3, 8.95) }, audio.Cuts);

        // every request names the job's model, both languages and the prompt of this way, and nothing of the diarize model;
        // the whole chunk names the languages in its own order: Russian first keeps Russian phrases Russian
        var requests = server.Requests;
        Assert.Equal(4, requests.Count);
        Assert.Single(requests, r => r.Whole);
        Assert.All(requests, r =>
        {
            Assert.Equal(new[] { Model }, r.Values("model"));
            Assert.Equal(r.Whole ? new[] { "ru", "es" } : new[] { "es", "ru" }, r.Values("languages[]"));
            Assert.Equal(new[] { "json" }, r.Values("response_format"));
            Assert.Contains("латиницей", Assert.Single(r.Values("prompt")));
            Assert.Empty(r.Values("language"));
            Assert.Empty(r.Values("temperature"));
            Assert.Empty(r.Values("chunking_strategy"));
        });

        var usage = Assert.IsAssignableFrom<IReadOnlyDictionary<string, object?>>(answer.RawResponse["usage"]);
        Assert.Equal("duration", usage["type"]);
        Assert.Equal(81.0, usage["seconds"]);

        var block = Block(answer.RawResponse);
        Assert.Equal(3, block["pieces"]);
        Assert.Equal(4, block["requests"]);
        Assert.Equal(18, block["words"]);
        Assert.Equal(18, block["words_matched"]);
        Assert.Equal(false, block["whole_text_missing"]);
        Assert.Equal(new object?[] { "es", "ru" }, (object?[])block["languages"]!);
        Assert.Equal(new object?[] { "ru", "es" }, (object?[])block["whole_languages"]!);
        Assert.Equal(
            "Speech pieces: 3 lines from 3 pieces, 18 of 18 words placed by a partner",
            SpeechPiecesTranscription.Describe(answer.RawResponse));
        Assert.Equal("Speech pieces: asking the whole chunk and its 3 pieces", Assert.Single(activity));
    }

    [Fact]
    public async Task Without_an_order_of_its_own_the_whole_chunk_names_the_languages_as_the_pieces_do()
    {
        var server = new ScriptedServer(seen => (HttpStatusCode.OK, seen.Whole
            ? Answer("Buenos días. Вот так. Hasta luego.", 60)
            : Answer(new[] { "Buenos dias", "Вот так", "Hasta luego" }[seen.Piece], 5)));
        var config = await LoadConfigAsync(new Dictionary<string, object?> { [SpeechPiecesTranscription.WholeLanguagesConfigKey] = null });

        var answer = await CreateWay(ThreePieces(), server).TranscribeAsync(NewChunk(), 0, config, ChunkOptions(config));

        Assert.Equal(4, server.Requests.Count);
        Assert.All(server.Requests, r => Assert.Equal(new[] { "es", "ru" }, r.Values("languages[]")));
        Assert.Equal(new object?[] { "es", "ru" }, (object?[])Block(answer.RawResponse)["whole_languages"]!);
    }

    [Fact]
    public async Task A_chunk_that_is_one_piece_is_asked_once_and_as_a_piece()
    {
        // speech 1–9 s of a 10 s chunk: one piece, so one request, and that short it names the languages as a piece does
        var server = new ScriptedServer(_ => (HttpStatusCode.OK, Answer("Hasta mañana.", 10)));
        var config = await LoadConfigAsync();

        var answer = await CreateWay(new FakeAudio { DurationSec = 10, Silence = new[] { Quiet(0, 1), Quiet(9, 10) } }, server)
            .TranscribeAsync(NewChunk(), 0, config, ChunkOptions(config));

        var request = Assert.Single(server.Requests);
        Assert.True(request.Whole);
        Assert.Equal(new[] { "es", "ru" }, request.Values("languages[]"));
        Assert.Equal("Hasta mañana.", Assert.Single(answer.Segments).Text);
    }

    [Fact]
    public async Task A_line_that_starts_in_the_repeat_of_the_previous_chunk_and_goes_on_starts_where_the_repeat_ends()
    {
        // speech 0–0.5 s (wholly in the repeated second), 2–6 s; in the second case speech 0–3 s crosses the repeat
        var config = await LoadConfigAsync();
        var server = new ScriptedServer(seen => (HttpStatusCode.OK, seen.Whole ? Answer("uno dos tres", 30) : Answer(seen.Piece == 0 ? "uno" : "dos tres", 3)));

        var inside = await CreateWay(new FakeAudio { DurationSec = 30, Silence = new[] { Quiet(0.5, 2), Quiet(6, 30) } }, server)
            .TranscribeAsync(NewChunk(), 1.0, config, ChunkOptions(config));
        // the line of the repeat keeps its own time: the writers drop it, the previous chunk has it
        Assert.Equal(new[] { (0.0, 0.5), (2.0, 6.0) }, inside.Segments.Select(s => (s.Start, s.End)));

        var crossing = await CreateWay(new FakeAudio { DurationSec = 30, Silence = new[] { Quiet(3, 5), Quiet(8, 30) } }, server)
            .TranscribeAsync(NewChunk(), 1.0, config, ChunkOptions(config));
        Assert.Equal(new[] { (1.0, 3.0), (5.0, 8.0) }, crossing.Segments.Select(s => (s.Start, s.End)));
    }

    [Fact]
    public async Task A_piece_the_whole_text_has_no_words_for_keeps_what_was_heard_in_it()
    {
        var server = new ScriptedServer(seen => (HttpStatusCode.OK, seen.Whole
            ? Answer("Buenos días. Hasta luego.", 60)
            : Answer(new[] { "Buenos dias", "Вот.", "Hasta luego" }[seen.Piece], 5)));
        var config = await LoadConfigAsync();

        var answer = await CreateWay(ThreePieces(), server).TranscribeAsync(NewChunk(), 0, config, ChunkOptions(config));

        Assert.Equal(new[] { "Buenos días.", "Вот.", "Hasta luego." }, answer.Segments.Select(s => s.Text));
        Assert.Equal(1, Block(answer.RawResponse)["pieces_kept_own_text"]);

        // three letters of sound are not asked again: there is nothing to respell
        Assert.Equal(4, server.Requests.Count);
        Assert.Equal(0, Block(answer.RawResponse)["pieces_asked_with_context"]);
    }

    // ---- a lone piece is spelled by what stands around it ----

    [Theory]
    [InlineData("Томар.", null, "tomar agua", "Tomar.")]
    [InlineData("И.", "primero", "y después", "Y.")]
    [InlineData("бебер", "quiero beber", "ahora", "beber")]
    [InlineData("Но.", "да", "nos vamos", null)]          // a short word must sound exactly the same
    [InlineData("Пока.", null, "пока нет", null)]         // the neighbour is Russian too: nothing to take
    [InlineData("Сейчас.", null, "seis horas", null)]     // not the same word
    public void A_lone_piece_that_repeats_its_neighbour_takes_the_neighbours_spelling(string own, string? previous, string? next, string? expected) =>
        Assert.Equal(expected, SpeechPieceSecondLook.SpelledAsNeighbour(own, previous, next));

    [Theory]
    [InlineData("Сьерра.", "Vamos a la playa. Sierra. Mañana por la tarde.", "Sierra.")]
    [InlineData("Керемос.", "Bueno. Y queremos, queremos salir.", "Queremos.")]         // said three times, written twice: found by sound, not by place
    [InlineData("Номеакуэрдо.", "Pero no me acuerdo, no me acuerdo.", "No me acuerdo.")] // the piece glued the words
    [InlineData("Подожди.", "Vamos a la playa. Ron. Mañana por la tarde.", null)]        // the answer left the piece out
    [InlineData("Вселенная.", "¿Del universo? Вселенная. Universo.", null)]              // the answer keeps the Russian word
    [InlineData("Ми но.", "Para mí. No quiero ir.", null)]                               // a run does not go over the end of a sentence
    public void A_lone_piece_is_spelled_as_the_run_of_the_answer_with_audio_around_that_sounds_like_it(string own, string around, string? expected) =>
        Assert.Equal(expected, SpeechPieceSecondLook.SpelledAsHeardAround(own, around));

    [Fact]
    public async Task A_lone_piece_that_says_the_first_word_of_the_next_line_is_spelled_as_that_line()
    {
        // the word is said twice; the text of the whole chunk has it once, at the line that goes on
        var server = new ScriptedServer(seen => (HttpStatusCode.OK, seen.Whole
            ? Answer("Quiero un vaso. Tomar agua fría.", 60)
            : Answer(new[] { "Quiero un vaso", "Томар.", "Tomar agua fria" }[seen.Piece], 5)));
        var config = await LoadConfigAsync();

        var answer = await CreateWay(ThreePieces(), server).TranscribeAsync(NewChunk(), 0, config, ChunkOptions(config));

        Assert.Equal(new[] { "Quiero un vaso.", "Tomar.", "Tomar agua fría." }, answer.Segments.Select(s => s.Text));
        Assert.Equal(4, server.Requests.Count); // the neighbour line said it: nothing more is asked
        var block = Block(answer.RawResponse);
        Assert.Equal(1, block["pieces_kept_own_text"]);
        Assert.Equal(1, block["pieces_spelled_by_neighbour"]);
        Assert.Equal(0, block["pieces_asked_with_context"]);
        var item = Assert.IsAssignableFrom<IReadOnlyDictionary<string, object?>>(((object?[])block["items"]!)[1]);
        Assert.Equal("Томар.", item["own_text"]);
        Assert.Equal("neighbour", item["second_look"]);
        Assert.EndsWith("; 1 lone pieces spelled by what stands around them", SpeechPiecesTranscription.Describe(answer.RawResponse));
    }

    [Fact]
    public async Task A_lone_piece_written_in_Cyrillic_is_asked_again_with_the_audio_around_it()
    {
        // the whole text leaves the middle word out; alone it is heard in Cyrillic, with its surroundings in Spanish
        const string withSurroundings = "Vamos a la playa. Sierra. Mañana por la tarde.";
        var audio = ThreePieces();
        var server = new ScriptedServer(seen => (HttpStatusCode.OK,
            seen.Whole ? Answer("Vamos a la playa. Mañana por la tarde.", 60)
            : seen.Context ? Answer(withSurroundings, 21)
            : Answer(new[] { "Vamos a la playa", "Сьерра.", "Manana por la tarde" }[seen.Piece], 5)));
        var config = await LoadConfigAsync();
        var activity = new List<string>();

        var answer = await CreateWay(audio, server).TranscribeAsync(NewChunk(), 0, config, ChunkOptions(config), activity.Add);

        Assert.Equal(new[] { "Vamos a la playa.", "Sierra.", "Mañana por la tarde." }, answer.Segments.Select(s => s.Text));
        Assert.Equal(new[] { (9.0, 20.0) }, answer.Segments.Where(s => s.Text == "Sierra.").Select(s => (s.Start, s.End)));

        // the piece 9–20 s with five seconds on each side, asked as any other request of the way
        Assert.Equal(new[] { (1, 4.0, 21.0) }, audio.ContextCuts);
        var again = Assert.Single(server.Requests, r => r.Context);
        Assert.Equal(1, again.Piece);
        Assert.Equal(new[] { Model }, again.Values("model"));
        Assert.Equal(new[] { "es", "ru" }, again.Values("languages[]"));
        Assert.Contains("Speech pieces: asking 1 lone pieces again with the audio around them", activity);

        var block = Block(answer.RawResponse);
        Assert.Equal(5, block["requests"]);
        Assert.Equal(1, block["pieces_asked_with_context"]);
        Assert.Equal(1, block["pieces_spelled_by_context"]);
        Assert.Equal(0, block["pieces_spelled_by_neighbour"]);
        var item = Assert.IsAssignableFrom<IReadOnlyDictionary<string, object?>>(((object?[])block["items"]!)[1]);
        Assert.Equal("Сьерра.", item["own_text"]);
        Assert.Equal(withSurroundings, item["context_text"]);
        Assert.Equal("context", item["second_look"]);

        var usage = Assert.IsAssignableFrom<IReadOnlyDictionary<string, object?>>(answer.RawResponse["usage"]);
        Assert.Equal(96.0, usage["seconds"]); // 60 whole + 3 × 5 pieces + 21 with the audio around
    }

    [Fact]
    public async Task An_answer_with_audio_around_that_heard_something_else_does_not_replace_the_piece()
    {
        var server = new ScriptedServer(seen => (HttpStatusCode.OK,
            seen.Whole ? Answer("Vamos a la playa. Mañana por la tarde.", 60)
            : seen.Context ? Answer("Vamos a la playa. Ron. Mañana por la tarde.", 21)
            : Answer(new[] { "Vamos a la playa", "Подожди.", "Manana por la tarde" }[seen.Piece], 5)));
        var config = await LoadConfigAsync();

        var answer = await CreateWay(ThreePieces(), server).TranscribeAsync(NewChunk(), 0, config, ChunkOptions(config));

        Assert.Equal(new[] { "Vamos a la playa.", "Подожди.", "Mañana por la tarde." }, answer.Segments.Select(s => s.Text));
        var block = Block(answer.RawResponse);
        Assert.Equal(1, block["pieces_asked_with_context"]);
        Assert.Equal(0, block["pieces_spelled_by_context"]);
        var item = Assert.IsAssignableFrom<IReadOnlyDictionary<string, object?>>(((object?[])block["items"]!)[1]);
        Assert.False(item.ContainsKey("second_look"));
    }

    [Fact]
    public async Task With_no_seconds_of_audio_around_set_a_lone_piece_is_not_asked_again()
    {
        var server = new ScriptedServer(seen => (HttpStatusCode.OK, seen.Whole
            ? Answer("Vamos a la playa. Mañana por la tarde.", 60)
            : Answer(new[] { "Vamos a la playa", "Сьерра.", "Manana por la tarde" }[seen.Piece], 5)));
        var config = await LoadConfigAsync(new Dictionary<string, object?> { [SpeechPiecesTranscription.ContextConfigKey] = 0 });

        var answer = await CreateWay(ThreePieces(), server).TranscribeAsync(NewChunk(), 0, config, ChunkOptions(config));

        Assert.Equal(new[] { "Vamos a la playa.", "Сьерра.", "Mañana por la tarde." }, answer.Segments.Select(s => s.Text));
        Assert.Equal(4, server.Requests.Count);
    }

    // ---- a line the whole text gave in Spanish where the piece alone is Russian ----

    [Theory]
    [InlineData("Esto ya es pasado.", "Это уже прошлое.", true)]
    [InlineData("Queremos salir.", "Керемос салир.", false)]           // the same speech in two spellings sounds alike
    [InlineData("Mhm.", "Угу.", false)]                                // too little said to doubt a line by it
    [InlineData("Это уже прошлое.", "Esto ya es pasado.", false)]      // only a Spanish line over a Russian piece is doubted
    [InlineData("Bueno, давай дальше.", "Ну, давай дальше.", false)]   // one word differs: the line is as Russian as the piece
    [InlineData("Claro. Y...", "Ага, ага, ну да, claro. И...", false)]  // the line is a part of what the piece heard, word for word
    [InlineData("elemental...", "Это элементарный пример.", true)]     // a translation that only resembles a part stays doubted
    [InlineData("Esto ya es pasado.", null, false)]
    [InlineData("", "Это уже прошлое.", false)]
    public void A_Spanish_line_over_a_Russian_piece_that_sounds_unlike_it_may_be_a_translation(string? line, string? own, bool expected) =>
        Assert.Equal(expected, SpeechPieceSecondLook.LooksTranslated(line, own));

    [Theory]
    [InlineData("Это уже прошлое.", "Esto ya es pasado.", "Sí, claro. Это уже прошлое. Vamos.", "Это уже прошлое.")]
    [InlineData("Это уже прошлое.", "Esto ya es pasado.", "Sí, claro, это уже прошлое, vamos.", "Это уже прошлое.")]   // cut out of the middle of a sentence
    [InlineData("Не помню, но знаю", "No recuerdo pero sé", "Bueno. Не помню, но знаю, да.", "Не помню, но знаю")]      // the marks inside the run stay
    [InlineData("Это уже прошлое.", "Esto ya es pasado.", "Sí, claro. Esto ya es pasado. Vamos.", null)]               // the answer stands by the line
    [InlineData("Это уже прошлое.", "Esto ya es pasado.", "Sí, claro. Vamos a ver.", null)]                            // the answer has neither
    [InlineData("Омер, комо, коме.", "Comer, como, come.", "El verbo comer, como, come mucho.", null)]                 // sounds like both: nearer to the line
    [InlineData("О, мато.", "oh, más.", "¿Y después? Oh, mato. No sé.", null)]                                         // a run without a Russian word: the piece is Spanish, the line stays
    [InlineData("Сьерра, сьерра.", "no, no, no, no, no, no", "Vamos a la montaña. Sierra, sierra. Ajá.", null)]              // nor when the answer has nothing like the line: tried, took more wrong lines than right
    [InlineData("Смотри, repartir — это раздавать.", "mira, repartir es dar", "Смотри, репартир это раздавать. Vale.", "Смотри, repartir это раздавать.")]   // a word the piece alone wrote in Latin keeps it
    [InlineData("Это проблема, problema.", "Es un problema, problema.", "Да. Это проблема, проблема. Vale.", "Это проблема, проблема.")]                     // said in both languages: two words, nothing respelled
    public void A_doubted_line_is_replaced_by_the_run_of_the_answer_with_audio_around_that_sounds_like_the_piece(
        string own, string line, string around, string? expected) =>
        Assert.Equal(expected, SpeechPieceSecondLook.SaidAsHeardAround(own, line, around));

    // ---- the mirror: a line the whole text gave in Russian where the piece alone is Spanish ----

    [Theory]
    [InlineData("Может быть, завтра.", "Tal vez mañana.", true)]
    [InlineData("Керемос салир.", "Queremos salir.", false)]                                  // the same speech in two spellings sounds alike
    [InlineData("Угу.", "Mhm.", false)]                                                      // too little said to doubt a line by it
    [InlineData("Tal vez mañana.", "Может быть, завтра.", false)]                             // that is the other doubt
    [InlineData("comprar pan... так, comprar, comprar, а дальше...", "Comprar pan.", false)]  // the piece alone caught a part of the line, word for word
    [InlineData("Нет.", "Нет, mañana no.", false)]                                            // the line is a part of what the piece heard: the rest stands in the next line
    [InlineData("15 o", "quince o.", false)]                                                // no Russian word in the line: a number in figures
    [InlineData("Может быть, завтра.", null, false)]
    [InlineData("", "Tal vez mañana.", false)]
    public void A_Russian_line_over_a_Spanish_piece_that_sounds_unlike_it_may_be_Spanish_speech(string? line, string? own, bool expected) =>
        Assert.Equal(expected, SpeechPieceSecondLook.LooksWrittenInRussian(line, own));

    [Theory]
    [InlineData("Para comer, claro.", "Согласен? Claro.", "Они так делают. Para comer, claro. Ну давай.", "Para comer, claro.")]
    [InlineData("Tal vez mañana llueva.", "Может быть, mañana", "Bien. Tal vez mañana llueve, llueve.", "Tal vez mañana llueve.")]   // cut out of a sentence: the piece's end mark
    [InlineData("Luego, ya.", "потом", "Y mi trabajo, потом, estaba mal.", null)]                                                  // the Russian word of the line was said
    [InlineData("Variable.", "viable. Вот", "¿Cuál? Variable. Вот и пример.", null)]                                               // so was this one: the line holds more than the piece
    [InlineData("Problema.", "то.", "Ага. Проблема. Давай дальше.", null)]                                                         // a run without a Spanish word: the piece alone wrote Russian in Latin
    [InlineData("Lara camino.", "неровный путь,", "Не как раньше. А. Вот. Camino, segundo... sí.", null)]                           // alike, but not enough for this doubt
    [InlineData("Tal vez mañana.", "Может быть, завтра.", "Bien. Vamos a ver.", null)]                                              // the answer has neither
    public void A_Russian_line_is_replaced_by_the_Spanish_run_of_the_answer_with_audio_around_that_sounds_like_the_piece(
        string own, string line, string around, string? expected) =>
        Assert.Equal(expected, SpeechPieceSecondLook.SaidInSpanishAround(own, line, around));

    [Fact]
    public async Task A_Russian_line_over_a_Spanish_piece_is_taken_from_the_piece_asked_again_with_the_audio_around_it()
    {
        // the whole text gives the Spanish phrase of the middle piece in Russian; alone and with its surroundings it is Spanish
        const string withSurroundings = "Buenos días. Tal vez mañana. Hasta";
        var audio = ThreePieces();
        var server = new ScriptedServer(seen => (HttpStatusCode.OK,
            seen.Whole ? Answer("Buenos días. Может быть, завтра. Hasta luego.", 60)
            : seen.Context ? Answer(withSurroundings, 21)
            : Answer(new[] { "Buenos dias", "Tal vez mañana", "Hasta luego" }[seen.Piece], 5)));
        var config = await LoadConfigAsync();
        var activity = new List<string>();

        var answer = await CreateWay(audio, server).TranscribeAsync(NewChunk(), 0, config, ChunkOptions(config), activity.Add);

        Assert.Equal(new[] { "Buenos días.", "Tal vez mañana.", "Hasta luego." }, answer.Segments.Select(s => s.Text));
        Assert.Equal(new[] { (1, 4.0, 21.0) }, audio.ContextCuts);
        Assert.Contains("Speech pieces: asking 1 pieces whose line may be a translation again with the audio around them", activity);

        var block = Block(answer.RawResponse);
        Assert.Equal(5, block["requests"]);
        Assert.Equal(1, block["lines_asked_with_context"]);
        Assert.Equal(1, block["lines_taken_from_context"]);
        var item = Assert.IsAssignableFrom<IReadOnlyDictionary<string, object?>>(((object?[])block["items"]!)[1]);
        Assert.Equal("Tal vez mañana", item["own_text"]);
        Assert.Equal(withSurroundings, item["context_text"]);
        Assert.Equal("Может быть, завтра.", item["whole_text_line"]);
        Assert.Equal("line", item["second_look"]);
    }

    [Fact]
    public async Task A_Russian_line_stays_when_the_piece_asked_again_with_the_audio_around_it_has_its_Russian_word()
    {
        // the piece alone put a Russian phrase into Spanish; with its surroundings it is the Russian of the whole text
        var server = new ScriptedServer(seen => (HttpStatusCode.OK,
            seen.Whole ? Answer("Buenos días. Может быть, завтра. Hasta luego.", 60)
            : seen.Context ? Answer("Buenos días. Может быть, завтра. Hasta", 21)
            : Answer(new[] { "Buenos dias", "Tal vez mañana", "Hasta luego" }[seen.Piece], 5)));
        var config = await LoadConfigAsync();

        var answer = await CreateWay(ThreePieces(), server).TranscribeAsync(NewChunk(), 0, config, ChunkOptions(config));

        Assert.Equal(new[] { "Buenos días.", "Может быть, завтра.", "Hasta luego." }, answer.Segments.Select(s => s.Text));
        var block = Block(answer.RawResponse);
        Assert.Equal(1, block["lines_asked_with_context"]);
        Assert.Equal(0, block["lines_taken_from_context"]);
        var item = Assert.IsAssignableFrom<IReadOnlyDictionary<string, object?>>(((object?[])block["items"]!)[1]);
        Assert.False(item.ContainsKey("whole_text_line"));
        Assert.False(item.ContainsKey("second_look"));
    }

    [Fact]
    public async Task A_line_that_may_be_a_translation_is_taken_from_the_piece_asked_again_with_the_audio_around_it()
    {
        // the whole text gives the Russian phrase of the middle piece in Spanish; alone and with its surroundings it is Russian
        const string withSurroundings = "Buenos días. Это уже прошлое. Hasta";
        var audio = ThreePieces();
        var server = new ScriptedServer(seen => (HttpStatusCode.OK,
            seen.Whole ? Answer("Buenos días. Esto ya es pasado. Hasta luego.", 60)
            : seen.Context ? Answer(withSurroundings, 21)
            : Answer(new[] { "Buenos dias", "Это уже прошлое", "Hasta luego" }[seen.Piece], 5)));
        var config = await LoadConfigAsync();
        var activity = new List<string>();

        var answer = await CreateWay(audio, server).TranscribeAsync(NewChunk(), 0, config, ChunkOptions(config), activity.Add);

        Assert.Equal(new[] { "Buenos días.", "Это уже прошлое.", "Hasta luego." }, answer.Segments.Select(s => s.Text));

        // asked as a lone piece is: the piece 9–20 s with five seconds on each side, Spanish named first
        Assert.Equal(new[] { (1, 4.0, 21.0) }, audio.ContextCuts);
        var again = Assert.Single(server.Requests, r => r.Context);
        Assert.Equal(1, again.Piece);
        Assert.Equal(new[] { "es", "ru" }, again.Values("languages[]"));
        Assert.Contains("Speech pieces: asking 1 pieces whose line may be a translation again with the audio around them", activity);

        var block = Block(answer.RawResponse);
        Assert.Equal(5, block["requests"]);
        Assert.Equal(1, block["lines_asked_with_context"]);
        Assert.Equal(1, block["lines_taken_from_context"]);
        Assert.Equal(0, block["pieces_asked_with_context"]);
        Assert.Equal(0, block["pieces_kept_own_text"]);
        var item = Assert.IsAssignableFrom<IReadOnlyDictionary<string, object?>>(((object?[])block["items"]!)[1]);
        Assert.Equal("Это уже прошлое", item["own_text"]);
        Assert.Equal(withSurroundings, item["context_text"]);
        Assert.Equal("Esto ya es pasado.", item["whole_text_line"]);
        Assert.Equal("line", item["second_look"]);
        Assert.EndsWith("; 1 lines the whole text gave in the other language taken from the piece asked again",
            SpeechPiecesTranscription.Describe(answer.RawResponse));

        var usage = Assert.IsAssignableFrom<IReadOnlyDictionary<string, object?>>(answer.RawResponse["usage"]);
        Assert.Equal(96.0, usage["seconds"]); // 60 whole + 3 × 5 pieces + 21 with the audio around
    }

    [Fact]
    public async Task A_doubted_line_stays_when_the_piece_asked_again_with_the_audio_around_it_says_the_same()
    {
        // the piece alone misheard: with its surroundings it is the Spanish phrase of the whole text
        var server = new ScriptedServer(seen => (HttpStatusCode.OK,
            seen.Whole ? Answer("Buenos días. Esto ya es pasado. Hasta luego.", 60)
            : seen.Context ? Answer("Buenos días. Esto ya es pasado. Hasta", 21)
            : Answer(new[] { "Buenos dias", "Это уже прошлое", "Hasta luego" }[seen.Piece], 5)));
        var config = await LoadConfigAsync();

        var answer = await CreateWay(ThreePieces(), server).TranscribeAsync(NewChunk(), 0, config, ChunkOptions(config));

        Assert.Equal(new[] { "Buenos días.", "Esto ya es pasado.", "Hasta luego." }, answer.Segments.Select(s => s.Text));
        var block = Block(answer.RawResponse);
        Assert.Equal(5, block["requests"]);
        Assert.Equal(1, block["lines_asked_with_context"]);
        Assert.Equal(0, block["lines_taken_from_context"]);
        var item = Assert.IsAssignableFrom<IReadOnlyDictionary<string, object?>>(((object?[])block["items"]!)[1]);
        Assert.Equal("Buenos días. Esto ya es pasado. Hasta", item["context_text"]);
        Assert.False(item.ContainsKey("whole_text_line"));
        Assert.False(item.ContainsKey("second_look"));
        Assert.DoesNotContain("taken from the piece asked again", SpeechPiecesTranscription.Describe(answer.RawResponse));
    }

    [Fact]
    public async Task With_no_seconds_of_audio_around_set_a_doubted_line_is_not_asked_again()
    {
        var server = new ScriptedServer(seen => (HttpStatusCode.OK, seen.Whole
            ? Answer("Buenos días. Esto ya es pasado. Hasta luego.", 60)
            : Answer(new[] { "Buenos dias", "Это уже прошлое", "Hasta luego" }[seen.Piece], 5)));
        var config = await LoadConfigAsync(new Dictionary<string, object?> { [SpeechPiecesTranscription.ContextConfigKey] = 0 });

        var answer = await CreateWay(ThreePieces(), server).TranscribeAsync(NewChunk(), 0, config, ChunkOptions(config));

        Assert.Equal(new[] { "Buenos días.", "Esto ya es pasado.", "Hasta luego." }, answer.Segments.Select(s => s.Text));
        Assert.Equal(4, server.Requests.Count);
        Assert.Equal(0, Block(answer.RawResponse)["lines_asked_with_context"]);
    }

    [Fact]
    public async Task Without_an_answer_for_the_whole_chunk_the_pieces_give_their_own_text()
    {
        var server = new ScriptedServer(seen => seen.Whole
            ? (HttpStatusCode.InternalServerError, ServerError)
            : (HttpStatusCode.OK, Answer(new[] { "uno", "dos", "tres" }[seen.Piece], 5)));
        var config = await LoadConfigAsync();

        var answer = await CreateWay(ThreePieces(), server).TranscribeAsync(NewChunk(), 0, config, ChunkOptions(config));

        Assert.Equal(new[] { "uno", "dos", "tres" }, answer.Segments.Select(s => s.Text));
        Assert.Equal("uno dos tres", answer.RawResponse["text"]);
        Assert.Equal(true, Block(answer.RawResponse)["whole_text_missing"]);
        Assert.Contains("no answer for the whole chunk", SpeechPiecesTranscription.Describe(answer.RawResponse));
        // the whole chunk: three tries of the way, three requests of the client in each; never another model
        Assert.Equal(9, server.Requests.Count(r => r.Whole));
        Assert.All(server.Requests, r => Assert.Equal(new[] { Model }, r.Values("model")));
    }

    [Fact]
    public async Task Words_of_a_piece_without_an_answer_stay_with_the_piece_before_it()
    {
        var server = new ScriptedServer(seen => seen.Whole
            ? (HttpStatusCode.OK, Answer("uno dos tres cuatro cinco seis", 60))
            : seen.Piece == 1
                ? (HttpStatusCode.BadRequest, """{ "error": { "message": "bad audio" } }""")
                : (HttpStatusCode.OK, Answer(seen.Piece == 0 ? "uno dos" : "cinco seis", 5)));
        var config = await LoadConfigAsync();

        var answer = await CreateWay(ThreePieces(), server).TranscribeAsync(NewChunk(), 0, config, ChunkOptions(config));

        Assert.Equal(
            new[] { new ASRSegment(2.0, 8.0, "uno dos tres cuatro"), new ASRSegment(21.5, 30.0, "cinco seis") },
            answer.Segments);
        Assert.Equal(1, Block(answer.RawResponse)["pieces_without_answer"]);
    }

    [Theory]
    [InlineData(200, 60.0, false)]   // fast speech
    [InlineData(380, 60.0, false)]   // 6 words a second and 20 spare
    [InlineData(381, 60.0, true)]
    [InlineData(21, 0.0, true)]
    [InlineData(0, 5.0, false)]
    public void An_answer_with_more_words_than_can_be_said_is_a_loop(int words, double audioSec, bool expected) =>
        Assert.Equal(expected, SpeechPiecesTranscription.LooksRunaway(
            string.Join(" ", Enumerable.Range(0, words).Select(i => i % 2 == 0 ? "uno" : "dos")), audioSec));

    [Theory]
    [InlineData(8, false)]    // a learner repeating a word
    [InlineData(19, false)]
    [InlineData(20, true)]
    [InlineData(131, true)]
    public void An_answer_with_one_word_many_times_in_a_row_is_a_loop(int times, bool expected)
    {
        // an ordinary answer but for the row; the marks and the capital letter do not make another word
        var row = string.Join(" ", Enumerable.Range(0, times).Select(i => i == 0 ? "No," : i % 3 == 0 ? "no..." : "no,"));
        var text = $"Vamos a la playa. {row} … Mañana por la tarde.";

        Assert.Equal(expected, SpeechPiecesTranscription.LooksRunaway(text, 158.0));
    }

    [Fact]
    public async Task An_answer_of_one_word_repeated_is_dropped_and_the_chunk_is_asked_again()
    {
        var wholeAsked = 0;
        var server = new ScriptedServer(seen =>
        {
            if (!seen.Whole)
                return (HttpStatusCode.OK, Answer(new[] { "uno dos", "tres cuatro", "cinco seis" }[seen.Piece], 5));
            return Interlocked.Increment(ref wholeAsked) == 1
                ? (HttpStatusCode.OK, Answer(string.Join(" ", Enumerable.Repeat("¿Pan?", 3000)), 60))
                : (HttpStatusCode.OK, Answer("Uno, dos. Tres, cuatro. Cinco, seis.", 60));
        });
        var config = await LoadConfigAsync();

        var answer = await CreateWay(ThreePieces(), server).TranscribeAsync(NewChunk(), 0, config, ChunkOptions(config));

        Assert.Equal(new[] { "Uno, dos.", "Tres, cuatro.", "Cinco, seis." }, answer.Segments.Select(s => s.Text));
        Assert.Equal(2, wholeAsked);
        var block = Block(answer.RawResponse);
        Assert.Equal(1, block["runaway_answers"]);
        Assert.Equal(5, block["requests"]);
        Assert.Equal(false, block["whole_text_missing"]);
        // the dropped answer was paid for: 60 + 60 for the whole chunk, 5 for each piece
        var usage = Assert.IsAssignableFrom<IReadOnlyDictionary<string, object?>>(answer.RawResponse["usage"]);
        Assert.Equal(135.0, usage["seconds"]);
        Assert.Contains("1 answers of a repeated word dropped and asked again", SpeechPiecesTranscription.Describe(answer.RawResponse));
    }

    [Fact]
    public async Task A_whole_text_that_is_not_the_speech_of_the_pieces_is_not_used()
    {
        var server = new ScriptedServer(seen => (HttpStatusCode.OK, seen.Whole
            ? Answer("Gracias por ver el video. Suscríbete al canal y dale a me gusta.", 60)
            : Answer(new[] { "Hoy vamos a repetir", "el gerundio de los verbos", "давайте начнём с глагола" }[seen.Piece], 5)));
        var config = await LoadConfigAsync();

        var answer = await CreateWay(ThreePieces(), server).TranscribeAsync(NewChunk(), 0, config, ChunkOptions(config));

        Assert.Equal(
            new[] { "Hoy vamos a repetir", "el gerundio de los verbos", "давайте начнём с глагола" },
            answer.Segments.Select(s => s.Text));
        var block = Block(answer.RawResponse);
        Assert.Equal(true, block["whole_text_rejected"]);
        Assert.Equal(true, block["whole_text_missing"]);
        Assert.Contains("did not match the pieces", SpeechPiecesTranscription.Describe(answer.RawResponse));
    }

    [Fact]
    public async Task Without_any_answer_the_chunk_fails()
    {
        var server = new ScriptedServer(_ => (HttpStatusCode.BadRequest, """{ "error": { "message": "bad audio" } }"""));
        var config = await LoadConfigAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => CreateWay(ThreePieces(), server).TranscribeAsync(NewChunk(), 0, config, ChunkOptions(config)));
    }

    [Fact]
    public async Task A_chunk_of_one_piece_is_asked_once()
    {
        var audio = new FakeAudio { DurationSec = 20, Silence = new[] { Quiet(0, 1.5), Quiet(12, 20) } };
        var server = new ScriptedServer(_ => (HttpStatusCode.OK, Answer("Una frase sola.", 20)));
        var config = await LoadConfigAsync();

        var answer = await CreateWay(audio, server).TranscribeAsync(NewChunk(), 0, config, ChunkOptions(config));

        Assert.Equal(new[] { new ASRSegment(1.5, 12.0, "Una frase sola.") }, answer.Segments);
        Assert.Single(server.Requests);
        Assert.Empty(audio.Cuts);
    }

    [Fact]
    public async Task A_silent_chunk_is_not_asked_and_gives_no_line()
    {
        var audio = new FakeAudio { DurationSec = 20, Silence = new[] { Quiet(0, 20) } };
        var server = new ScriptedServer(_ => (HttpStatusCode.OK, Answer("no", 1)));
        var config = await LoadConfigAsync();

        var answer = await CreateWay(audio, server).TranscribeAsync(NewChunk(), 0, config, ChunkOptions(config));

        Assert.Empty(answer.Segments);
        Assert.Empty(server.Requests);
        Assert.True(ServerErrorChunkSplit.HasTimeMarks(answer.RawResponse)); // an empty list of lines, not a line without time
    }

    [Theory]
    [InlineData(true, 1.0, 1.0, 0.3)]    // the shared config: pauses of 1 s are kept, the search finds pauses from 0.3 s
    [InlineData(true, 0.5, 0.5, 0.3)]    // a shorter kept pause is the longest pause there is
    [InlineData(true, 0.2, 0.2, 0.15)]   // and the search must find pauses that short
    [InlineData(false, 0.2, 1.0, 0.3)]   // without compression the pauses of the recording are as they were
    public async Task The_pause_that_ends_a_line_follows_the_kept_pause_of_the_job(bool compress, double keep, double pause, double searchFrom)
    {
        var config = await LoadConfigAsync(new Dictionary<string, object?>
        {
            ["silence_compress_enabled"] = compress,
            ["silence_keep_sec"] = keep,
        });

        var settings = SpeechPiecesTranscription.Settings.FromConfig(config);

        Assert.Equal(pause, settings.PauseSec, 3);
        Assert.Equal(searchFrom, settings.Detect.MinDurationSec, 3);
        Assert.Equal(new[] { "es", "ru" }, settings.Languages);
        Assert.Equal(new[] { "ru", "es" }, settings.WholeLanguages);
        Assert.Equal(15.0, settings.MaxPieceSec);
        Assert.Equal(3, settings.ParallelRequests);
    }

    // ---- the client ----

    [Fact]
    public async Task An_exact_request_is_not_repeated_in_another_form_or_by_another_model()
    {
        var server = new ScriptedServer(_ => (HttpStatusCode.InternalServerError, ServerError));
        var client = CreateClient(server);

        await Assert.ThrowsAsync<InvalidOperationException>(() => client.TranscribeAsync(NewChunk(), new TranscriptionClientOptions
        {
            Model = Model,
            Language = "es",
            Languages = new[] { "es", "ru" },
            ResponseFormat = "json",
            ExactRequest = true,
        }));

        Assert.Equal(3, server.Requests.Count);
        Assert.All(server.Requests, r =>
        {
            Assert.Equal(new[] { Model }, r.Values("model"));
            Assert.Equal(new[] { "json" }, r.Values("response_format"));
            Assert.Equal(new[] { "es", "ru" }, r.Values("languages[]"));
            Assert.Empty(r.Values("language"));
        });
    }

    // ---- inside the pipeline ----

    private TranscriptionPipeline CreatePipeline(FakeAudio audio, ScriptedServer server)
    {
        var artifacts = new ProjectArtifactService(
            new JobArtifactRootRegistry(),
            new ConfigurationBuilder().Build(),
            new PerJobCancellationManagerFactory(),
            new AudioUtils(),
            new TranscriptionOutputWriter(NullLogger<TranscriptionOutputWriter>.Instance),
            new WorkspaceRoot(Path.GetTempPath()),
            new TestHostEnvironment(),
            NullLogger<ProjectArtifactService>.Instance);
        return new TranscriptionPipeline(audio, new TranscriptionCache(Path.Combine(_root, "cache")), CreateClient(server), new TranscriptionMerger(), artifacts, null, null);
    }

    [Fact]
    public async Task A_job_with_the_shared_model_goes_the_usual_way()
    {
        var server = new ScriptedServer(_ => (HttpStatusCode.OK, """{ "text": "hola", "segments": [ { "start": 0.5, "end": 1.2, "text": "hola", "speaker": "A" } ] }"""));
        var audio = ThreePieces();
        var config = await TranscriptionConfig.FromFileAsync(Path.Combine(AppContext.BaseDirectory, "config", "default.json"));

        var answer = await CreatePipeline(audio, server).TranscribeChunkOrItsPartsAsync(config, NewChunk(), ChunkOptions(config), CancellationToken.None);

        var request = Assert.Single(server.Requests);
        Assert.Equal(new[] { "gpt-4o-transcribe-diarize" }, request.Values("model"));
        Assert.Equal(new[] { "es" }, request.Values("language"));
        Assert.Empty(request.Values("languages[]"));
        Assert.Empty(request.Values("prompt")); // the prompt of the new way does not reach the diarize model
        Assert.Empty(audio.Searches);
        Assert.False(answer.RawResponse.ContainsKey(SpeechPiecesTranscription.RawBlockKey));
    }

    /// <summary>
    /// The operator's "retranscribe" of one chunk of a job whose model answers plain text: the chunk's JSON
    /// gets lines with times, and the rebuilt Markdown shows them on the timeline of the recording.
    /// </summary>
    [Fact]
    public async Task Retranscribe_of_a_chunk_writes_lines_with_times_of_its_pieces()
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
        await File.WriteAllTextAsync(
            Path.Combine(jsonDir, "lesson_part_000.json"),
            """{ "text": "primero", "segments": [ { "id": 0, "start": 3.0, "end": 4.0, "text": "primero" } ] }""");

        // chunk 1: speech 0–4 s (starts in the second that repeats chunk 0 and goes on) and 6–9 s
        var audio = new FakeAudio { DurationSec = 30, Silence = new[] { Quiet(4, 6), Quiet(9, 30) } };
        var server = new ScriptedServer(seen => (HttpStatusCode.OK, seen.Whole
            ? Answer("Bien, seguimos con el verbo. Теперь прошедшее время.", 30)
            : Answer(seen.Piece == 0 ? "Bien seguimos con el verbo" : "Теперь прошедшее время", 4)));
        var config = await LoadConfigAsync();
        var pipeline = CreatePipeline(audio, server);

        await pipeline.RetranscribeMainChunkAsync(config, _root, "job1", 1, null, CancellationToken.None);

        using (var doc = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(jsonDir, "lesson_part_001.json"))))
        {
            var lines = doc.RootElement.GetProperty("segments").EnumerateArray()
                .Select(s => (s.GetProperty("start").GetDouble(), s.GetProperty("text").GetString()!)).ToList();
            Assert.Equal(new[] { (1.0, "Bien, seguimos con el verbo."), (6.0, "Теперь прошедшее время.") }, lines);
            Assert.Equal(2, doc.RootElement.GetProperty(SpeechPiecesTranscription.RawBlockKey).GetProperty("pieces").GetInt32());
        }

        await pipeline.RebuildCombinedOutputsFromPerChunkJsonAsync(config, _root, "job1", null, CancellationToken.None);

        // chunk 1 starts at 157 s of the recording
        var md = await File.ReadAllTextAsync(Path.Combine(_root, "chunks_md", "lesson_part_001.md"));
        Assert.Contains("- 158.00 speaker_0: \"Bien, seguimos con el verbo.\"", md);
        Assert.Contains("- 163.00 speaker_0: \"Теперь прошедшее время.\"", md);
    }

    // ---- retranscribe and the md of the chunk ----

    /// <summary>
    /// A job of two chunks whose chunk 1 (from 157 s, one repeated second) was transcribed before: its JSON
    /// holds one line at 2 s, and its md holds <paramref name="mdOnDisk"/>. Returns the path of that md.
    /// </summary>
    private async Task<string> JobWithATranscribedChunkAsync(string mdOnDisk)
    {
        var chunksDir = Path.Combine(_root, "chunks");
        var jsonDir = Path.Combine(_root, "chunks_json");
        var mdDir = Path.Combine(_root, "chunks_md");
        foreach (var dir in new[] { chunksDir, jsonDir, mdDir })
            Directory.CreateDirectory(dir);
        var infos = new[]
        {
            new ChunkInfo(Path.Combine(chunksDir, "lesson_part_000.wav"), 0.0, 0.0),
            new ChunkInfo(Path.Combine(chunksDir, "lesson_part_001.wav"), 157.0, 1.0),
        };
        File.WriteAllText(infos[0].Path, "chunk:0");
        File.WriteAllText(infos[1].Path, "chunk:1");
        await ChunkTimelineFile.SaveAsync(_root, infos, CancellationToken.None);
        await File.WriteAllTextAsync(
            Path.Combine(jsonDir, "lesson_part_001.json"),
            """{ "text": "texto de antes", "segments": [ { "id": 0, "start": 2.0, "end": 3.0, "text": "texto de antes" } ] }""");
        var mdPath = Path.Combine(mdDir, "lesson_part_001.md");
        await File.WriteAllTextAsync(mdPath, mdOnDisk);
        return mdPath;
    }

    private static ScriptedServer OneLineAgain() => new(seen => (HttpStatusCode.OK, Answer("Texto nuevo del trozo.", 30)));

    [Fact]
    public async Task Retranscribe_writes_the_md_of_the_chunk_anew_when_nobody_edited_it()
    {
        // the md is what the writer made of the previous answer; an editor only changed its line endings
        var mdPath = await JobWithATranscribedChunkAsync(">>>>>>>\r\n- 159.00 speaker_0: \"texto de antes\"\r\n<<<<<\r\n");
        var audio = new FakeAudio { DurationSec = 30, Silence = new[] { Quiet(0, 2), Quiet(6, 30) } };
        var config = await LoadConfigAsync();

        await CreatePipeline(audio, OneLineAgain()).RetranscribeMainChunkAsync(config, _root, "job1", 1, null, CancellationToken.None);

        Assert.Equal(">>>>>>>\n- 159.00 speaker_0: \"Texto nuevo del trozo.\"\n<<<<<\n", await File.ReadAllTextAsync(mdPath));
    }

    [Fact]
    public async Task Retranscribe_leaves_the_md_the_operator_edited()
    {
        const string edited = ">>>>>>>\n- 159.00 speaker_0: \"texto corregido a mano\"\n<<<<<\n";
        var mdPath = await JobWithATranscribedChunkAsync(edited);
        var audio = new FakeAudio { DurationSec = 30, Silence = new[] { Quiet(0, 2), Quiet(6, 30) } };
        var config = await LoadConfigAsync();

        await CreatePipeline(audio, OneLineAgain()).RetranscribeMainChunkAsync(config, _root, "job1", 1, null, CancellationToken.None);

        Assert.Equal(edited, await File.ReadAllTextAsync(mdPath));
        // the new answer is kept all the same: in the chunk's JSON
        Assert.Contains("Texto nuevo del trozo.", await File.ReadAllTextAsync(Path.Combine(_root, "chunks_json", "lesson_part_001.json")));
    }
}
