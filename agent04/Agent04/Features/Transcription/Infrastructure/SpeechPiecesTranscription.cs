using Agent04.Features.Transcription.Application;
using Agent04.Features.Transcription.Domain;
using Microsoft.Extensions.Logging;

namespace Agent04.Features.Transcription.Infrastructure;

/// <summary>
/// How a chunk is transcribed by a model that answers plain text (<c>gpt-transcribe</c>): no segments,
/// no time marks, no speakers. Such a model takes several languages and a prompt, so it writes Spanish
/// in Spanish and Russian in Russian inside one phrase; the diarize model takes one language and writes
/// the other one in the wrong script.
/// <para>
/// The chunk is asked twice. Once whole: a long stretch gives the best wording. Once in pieces cut at its
/// pauses (<see cref="SpeechPiecePlanner"/>): a piece has worse wording but a known place. The whole text is
/// laid onto the pieces word by word (<see cref="SpeechPieceWordAlignment"/>), and every piece becomes a
/// line that starts where the piece starts.
/// </para>
/// <para>
/// The answer has the shape of a server answer with time marks (<c>text</c>, <c>segments</c>,
/// <c>usage</c>), so the cache, <c>chunks_json</c>, the md writers and the merge of an operator split read
/// it as any other; the block <see cref="RawBlockKey"/> says that it was assembled here and from what.
/// There are no speakers in it: the way fits a recording of one voice (a participant track).
/// </para>
/// </summary>
public sealed class SpeechPiecesTranscription
{
    /// <summary>Config key: languages spoken in the recordings, for example <c>["es", "ru"]</c>.</summary>
    public const string LanguagesConfigKey = "speech_pieces_languages";

    /// <summary>Config key: the prompt of this way. The shared <c>prompt</c> key is not used: other models get that one.</summary>
    public const string PromptConfigKey = "speech_pieces_prompt";

    /// <summary>Config key: a pause this long ends a line.</summary>
    public const string PauseConfigKey = "speech_pieces_pause_sec";

    /// <summary>Config key: a line is ended at a shorter pause when it would grow longer than this.</summary>
    public const string MaxPieceConfigKey = "speech_pieces_max_sec";

    /// <summary>Config key: requests of one chunk that run at the same time.</summary>
    public const string ParallelConfigKey = "speech_pieces_parallel_requests";

    public const double DefaultPauseSec = 1.0;
    public const double DefaultMaxPieceSec = 15.0;
    public const int DefaultParallelRequests = 3;

    /// <summary>A piece with less sound than this is a click or a breath; it is not asked.</summary>
    public const double MinSpeechSec = 0.2;

    /// <summary>Audio given to the model before the first sound of a piece and after its last one.</summary>
    public const double LeadInSec = 0.2;
    public const double TailSec = 0.25;

    /// <summary>Key of the block in the chunk's answer that describes how it was assembled.</summary>
    public const string RawBlockKey = "speech_pieces";

    private const int Attempts = 3;

    /// <summary>
    /// Nobody speaks this fast. The model sometimes falls into repeating one word hundreds of times; an
    /// answer with more words than <c>audio seconds × this + <see cref="RunawaySpareWords"/></c> is such a
    /// loop, not speech. It is dropped and asked again: the loop does not repeat on the same audio.
    /// </summary>
    public const double RunawayWordsPerSec = 6.0;
    public const int RunawaySpareWords = 20;

    /// <summary>
    /// The text of the whole chunk is used when at least this share of the pieces' words found a partner
    /// in it (usually over 0.9). Below it the two are not the same speech, and the pieces are believed:
    /// each of them heard a few seconds and cannot run away far.
    /// </summary>
    public const double TrustedWordShare = 0.5;

    /// <summary>With fewer words than this in the pieces the share says nothing.</summary>
    public const int MinWordsToJudge = 10;

    private readonly IAudioUtils _audioUtils;
    private readonly ITranscriptionClient _client;
    private readonly ILogger? _logger;

    /// <summary>Pause before the second and the third try of one request is 1 and 2 of these. Tests set it to zero.</summary>
    internal TimeSpan RetryDelayUnit { get; set; } = TimeSpan.FromSeconds(2);

    public SpeechPiecesTranscription(IAudioUtils audioUtils, ITranscriptionClient client, ILogger? logger = null)
    {
        _audioUtils = audioUtils;
        _client = client;
        _logger = logger;
    }

    /// <summary>True for a model that answers plain text and takes several languages.</summary>
    public static bool AppliesTo(string? model) =>
        !string.IsNullOrWhiteSpace(model)
        && model.Trim().StartsWith("gpt-transcribe", StringComparison.OrdinalIgnoreCase);

    /// <summary>True when the text has more words than could be said in that much audio.</summary>
    internal static bool LooksRunaway(string? text, double audioSec)
    {
        if (string.IsNullOrEmpty(text))
            return false;
        var words = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
        return words > Math.Max(0, audioSec) * RunawayWordsPerSec + RunawaySpareWords;
    }

    /// <summary>What the way reads from the job config.</summary>
    internal sealed record Settings(
        IReadOnlyList<string> Languages,
        string? Prompt,
        double PauseSec,
        double MaxPieceSec,
        int ParallelRequests,
        SilenceDetectOptions Detect)
    {
        public static Settings FromConfig(TranscriptionConfig config)
        {
            var languages = (config.Get<object[]>(LanguagesConfigKey) ?? Array.Empty<object>())
                .Select(x => x?.ToString()?.Trim() ?? "")
                .Where(x => x.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            var prompt = config.Get<string>(PromptConfigKey);
            var pause = Math.Clamp(config.Get<double?>(PauseConfigKey) ?? DefaultPauseSec, 0.1, 30.0);
            var maxPiece = Math.Clamp(config.Get<double?>(MaxPieceConfigKey) ?? DefaultMaxPieceSec, 2.0, 300.0);
            var parallel = Math.Clamp(config.Get<int?>(ParallelConfigKey) ?? DefaultParallelRequests, 1, 16);

            // Silence compression leaves no pause longer than the one it keeps: with a kept pause below
            // pause_sec every long pause of the recording has that shorter length in the chunk.
            var silence = SilenceJobSettings.FromConfig(config);
            if (silence.CompressOn && silence.KeepSilenceSec > 0 && silence.KeepSilenceSec < pause)
                pause = Math.Max(0.1, silence.KeepSilenceSec);

            // A pause must be found to end a line: the search may not skip pauses of that length.
            var detect = silence.DetectOptions;
            var findFrom = Math.Max(0.05, pause - 0.05);
            if (detect.MinDurationSec > findFrom)
                detect = detect with { MinDurationSec = findFrom };

            return new Settings(languages, string.IsNullOrWhiteSpace(prompt) ? null : prompt, pause, maxPiece, parallel, detect);
        }
    }

    /// <param name="chunkPath">Audio of the chunk (or of a part of a chunk).</param>
    /// <param name="emitGuardSec">
    /// Seconds at the start that repeat the end of the previous chunk. A line that starts there and ends
    /// there belongs to the previous chunk and is dropped by the writers; a line that starts there and
    /// goes on gets the end of the repeat as its start, so that the writers keep it.
    /// </param>
    /// <param name="options">Model and log context of the chunk; the request fields are set here.</param>
    public async Task<TranscriptionClientResult> TranscribeAsync(
        string chunkPath,
        double emitGuardSec,
        TranscriptionConfig config,
        TranscriptionClientOptions options,
        Action<string>? activity = null,
        CancellationToken ct = default)
    {
        var settings = Settings.FromConfig(config);
        var ffmpeg = _audioUtils.WhichOr(config.FfmpegPath, "ffmpeg") ?? "ffmpeg";
        var ffprobe = _audioUtils.WhichOr(config.FfprobePath, "ffprobe") ?? "ffprobe";
        var durationSec = _audioUtils.GetDurationAndSize(ffprobe, chunkPath).DurationSeconds;
        var silence = _audioUtils.DetectSilence(ffmpeg, chunkPath, settings.Detect, ct);
        var pieces = SpeechPiecePlanner.Plan(silence, durationSec, settings.PauseSec, settings.MaxPieceSec, MinSpeechSec);
        var model = string.IsNullOrWhiteSpace(options.Model) ? config.Model : options.Model.Trim();

        if (pieces.Count == 0)
        {
            // Nothing but silence: no request, no line.
            _logger?.LogInformation("Chunk {ChunkIndex}: no speech found in {Duration:F1}s; nothing is asked", options.ChunkIndex, durationSec);
            activity?.Invoke("Speech pieces: no speech found, nothing is asked");
            return Answer(model, settings, durationSec, "", Array.Empty<object?>(), Array.Empty<object?>(),
                new List<IReadOnlyDictionary<string, object?>>(), requests: 0, words: 0, matched: 0,
                wholeTextMissing: false, wholeTextRejected: false, runawayAnswers: 0, withoutAnswer: 0, keptOwnText: 0);
        }

        var request = options.Clone();
        request.Model = model;
        request.Languages = settings.Languages.Count > 0 ? settings.Languages : null;
        request.Prompt = settings.Prompt;
        request.Temperature = null;
        request.ResponseFormat = "json";
        request.ChunkingStrategy = null;
        request.ExactRequest = true;
        request.StopAtServerError = false;

        activity?.Invoke(pieces.Count == 1
            ? "Speech pieces: one piece, asking the whole chunk"
            : $"Speech pieces: asking the whole chunk and its {pieces.Count} pieces");

        var workDir = Path.Combine(Path.GetTempPath(), "agent04_speech_pieces_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workDir);
        try
        {
            using var gate = new SemaphoreSlim(settings.ParallelRequests, settings.ParallelRequests);
            var dropped = new List<TranscriptionClientResult>(); // runaway answers: not used, but paid for
            var wholeTask = AskAsync(chunkPath, durationSec, request, gate, null, dropped, ct);
            var pieceTasks = new List<Task<TranscriptionClientResult?>>();
            if (pieces.Count > 1)
            {
                var extension = Path.GetExtension(chunkPath);
                for (var k = 0; k < pieces.Count; k++)
                {
                    var piece = pieces[k];
                    var from = Math.Max(0, piece.StartSec - LeadInSec);
                    var to = Math.Min(durationSec, piece.EndSec + TailSec);
                    var piecePath = Path.Combine(workDir, $"piece_{k:D3}{extension}");
                    pieceTasks.Add(AskAsync(
                        piecePath,
                        to - from,
                        request,
                        gate,
                        () => _audioUtils.ExtractAudioSegmentCopyOrReencode(ffmpeg, chunkPath, from, to - from, piecePath),
                        dropped,
                        ct));
                }
            }

            var whole = await wholeTask.ConfigureAwait(false);
            var pieceAnswers = await Task.WhenAll(pieceTasks).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();

            var usages = new List<IReadOnlyDictionary<string, object?>>();
            AddUsage(usages, whole);
            foreach (var answer in pieceAnswers)
                AddUsage(usages, answer);
            foreach (var answer in dropped)
                AddUsage(usages, answer);

            var wholeText = whole == null ? null : TextOf(whole);
            var own = pieceAnswers.Select(a => a == null ? null : TextOf(a)).ToArray();
            var withoutAnswer = pieceAnswers.Count(a => a == null);
            if (whole == null && (pieces.Count == 1 || withoutAnswer == pieces.Count))
                throw new InvalidOperationException("Speech pieces: the server gave no answer for the chunk");

            // The whole text and the pieces must be the same speech; when they are not, the pieces are believed.
            SpeechPieceWordAlignment.Result? placed = null;
            var wholeTextRejected = false;
            if (wholeText != null && pieces.Count > 1)
            {
                placed = SpeechPieceWordAlignment.Assign(wholeText, own);
                if (placed.WordsPieces >= MinWordsToJudge && placed.Matched < TrustedWordShare * placed.WordsPieces)
                {
                    _logger?.LogWarning(
                        "Chunk {ChunkIndex}: the text of the whole chunk does not match its pieces ({Matched} of {PieceWords} words of the pieces found in it); the pieces' own text is used",
                        options.ChunkIndex, placed.Matched, placed.WordsPieces);
                    wholeTextRejected = true;
                    wholeText = null;
                    placed = null;
                }
            }

            var lines = new string[pieces.Count];
            Array.Fill(lines, "");
            int words = 0, matched = 0, keptOwnText = 0;
            if (wholeText == null)
            {
                // Without the whole text the pieces' own wording is all there is.
                for (var k = 0; k < pieces.Count; k++)
                    lines[k] = own[k] ?? "";
                keptOwnText = lines.Count(l => l.Length > 0);
            }
            else if (pieces.Count == 1)
            {
                lines[0] = wholeText;
            }
            else if (placed != null)
            {
                words = placed.WordsWhole;
                matched = placed.Matched;
                for (var k = 0; k < pieces.Count; k++)
                {
                    lines[k] = placed.Texts[k];
                    // A piece none of the whole text's words went to keeps what was heard in it alone.
                    if (lines[k].Length == 0 && !string.IsNullOrWhiteSpace(own[k]))
                    {
                        lines[k] = own[k]!.Trim();
                        keptOwnText++;
                    }
                }

                // No piece gave a word to hold on to: the whole text stands at the first piece that is kept.
                if (placed.WordsPieces == 0 && wholeText.Length > 0)
                {
                    var first = Enumerable.Range(0, pieces.Count).FirstOrDefault(k => pieces[k].EndSec > emitGuardSec);
                    lines[first] = wholeText;
                }
            }

            var segments = new List<object?>();
            var items = new List<object?>();
            for (var k = 0; k < pieces.Count; k++)
            {
                var piece = pieces[k];
                items.Add(new Dictionary<string, object?>
                {
                    ["start"] = Round(piece.StartSec),
                    ["end"] = Round(piece.EndSec),
                    ["own_text"] = own.Length > k ? own[k] : null,
                });
                if (lines[k].Length == 0)
                    continue;

                var start = piece.StartSec;
                if (start < emitGuardSec && piece.EndSec > emitGuardSec)
                    start = emitGuardSec;
                segments.Add(new Dictionary<string, object?>
                {
                    ["id"] = segments.Count,
                    ["start"] = Round(start),
                    ["end"] = Round(Math.Max(start, piece.EndSec)),
                    ["text"] = lines[k],
                });
            }

            _logger?.LogInformation(
                "Chunk {ChunkIndex}: speech pieces {Pieces}, lines {Lines}, words {Matched}/{Words} placed by a partner, pieces without answer {Missing}, whole text missing {WholeMissing}",
                options.ChunkIndex, pieces.Count, segments.Count, matched, words, withoutAnswer, wholeText == null);

            return Answer(model, settings, durationSec,
                wholeText ?? string.Join(" ", lines.Where(l => l.Length > 0)),
                segments.ToArray(), items.ToArray(), usages,
                requests: 1 + pieceTasks.Count + dropped.Count, words, matched,
                wholeTextMissing: wholeText == null, wholeTextRejected, runawayAnswers: dropped.Count, withoutAnswer, keptOwnText);
        }
        finally
        {
            try
            {
                Directory.Delete(workDir, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger?.LogWarning(ex, "Speech pieces: could not remove the piece files in {Dir}", workDir);
            }
        }
    }

    /// <summary>What the answer says about itself: lines, and how many words stand by a partner.</summary>
    internal static string Describe(IReadOnlyDictionary<string, object?> raw)
    {
        var lines = raw.TryGetValue("segments", out var s) && s is object[] arr ? arr.Length : 0;
        if (!raw.TryGetValue(RawBlockKey, out var b) || b is not IReadOnlyDictionary<string, object?> block)
            return $"Speech pieces: {lines} lines";
        var text = $"Speech pieces: {lines} lines from {block.GetValueOrDefault("pieces")} pieces, "
                   + $"{block.GetValueOrDefault("words_matched")} of {block.GetValueOrDefault("words")} words placed by a partner";
        if (block.GetValueOrDefault("whole_text_rejected") is true)
            text += "; the text of the whole chunk did not match the pieces, their own text is used";
        else if (block.GetValueOrDefault("whole_text_missing") is true)
            text += "; no answer for the whole chunk, the pieces' own text is used";
        if (block.GetValueOrDefault("runaway_answers") is int runaway and > 0)
            text += $"; {runaway} answers of a repeated word dropped and asked again";
        if (block.GetValueOrDefault("pieces_without_answer") is int missing and > 0)
            text += $"; {missing} pieces without answer";
        return text;
    }

    /// <summary>
    /// One request, tried up to three times. A piece is cut right before its request, inside the gate,
    /// so that no more files are cut than are asked. An answer that <see cref="LooksRunaway"/> counts as
    /// a failed try and goes to <paramref name="dropped"/>. Null when no try gave a usable answer.
    /// </summary>
    private async Task<TranscriptionClientResult?> AskAsync(
        string audioPath,
        double audioSec,
        TranscriptionClientOptions request,
        SemaphoreSlim gate,
        Action? cut,
        List<TranscriptionClientResult> dropped,
        CancellationToken ct)
    {
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            cut?.Invoke();
            for (var attempt = 1; ; attempt++)
            {
                Exception? failure = null;
                try
                {
                    var answer = await _client.TranscribeAsync(audioPath, request, ct).ConfigureAwait(false);
                    if (!LooksRunaway(TextOf(answer), audioSec))
                        return answer;

                    lock (dropped)
                        dropped.Add(answer);
                    _logger?.LogWarning(
                        "Speech pieces: the answer for {File} ({Seconds:F1}s of audio) is one word repeated, not speech; try {Attempt} of {Attempts}",
                        Path.GetFileName(audioPath), audioSec, attempt, Attempts);
                }
                catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
                {
                    failure = ex;
                }

                if (attempt >= Attempts)
                {
                    _logger?.LogWarning(failure, "Speech pieces: no usable answer for {File} after {Attempts} tries", Path.GetFileName(audioPath), Attempts);
                    return null;
                }

                await Task.Delay(RetryDelayUnit * attempt, ct).ConfigureAwait(false);
            }
        }
        finally
        {
            gate.Release();
        }
    }

    private static Dictionary<string, object?> AnswerBlock(
        string model,
        Settings settings,
        object?[] items,
        int requests,
        int words,
        int matched,
        bool wholeTextMissing,
        bool wholeTextRejected,
        int runawayAnswers,
        int withoutAnswer,
        int keptOwnText) => new()
    {
        ["model"] = model,
        ["languages"] = settings.Languages.Cast<object?>().ToArray(),
        ["pause_sec"] = Round(settings.PauseSec),
        ["max_piece_sec"] = Round(settings.MaxPieceSec),
        ["pieces"] = items.Length,
        ["requests"] = requests,
        ["words"] = words,
        ["words_matched"] = matched,
        ["whole_text_missing"] = wholeTextMissing,
        ["whole_text_rejected"] = wholeTextRejected,
        ["runaway_answers"] = runawayAnswers,
        ["pieces_without_answer"] = withoutAnswer,
        ["pieces_kept_own_text"] = keptOwnText,
        ["items"] = items,
    };

    private static TranscriptionClientResult Answer(
        string model,
        Settings settings,
        double durationSec,
        string text,
        object?[] segments,
        object?[] items,
        List<IReadOnlyDictionary<string, object?>> usages,
        int requests,
        int words,
        int matched,
        bool wholeTextMissing,
        bool wholeTextRejected,
        int runawayAnswers,
        int withoutAnswer,
        int keptOwnText)
    {
        var raw = new Dictionary<string, object?>
        {
            ["task"] = "transcribe",
            ["duration"] = Round(durationSec),
            ["text"] = text,
            ["segments"] = segments,
        };
        if (usages.Count > 0)
            raw["usage"] = ServerErrorChunkSplit.SumUsage(usages);
        raw[RawBlockKey] = AnswerBlock(model, settings, items, requests, words, matched, wholeTextMissing, wholeTextRejected, runawayAnswers, withoutAnswer, keptOwnText);

        return new TranscriptionClientResult
        {
            RawResponse = raw,
            Segments = OpenAITranscriptionClient.ParseSegments(raw),
        };
    }

    private static void AddUsage(List<IReadOnlyDictionary<string, object?>> usages, TranscriptionClientResult? answer)
    {
        if (answer != null
            && answer.RawResponse.TryGetValue("usage", out var usage)
            && usage is IReadOnlyDictionary<string, object?> usageDict)
        {
            usages.Add(usageDict);
        }
    }

    private static string TextOf(TranscriptionClientResult answer) =>
        answer.RawResponse.TryGetValue("text", out var text) ? (text?.ToString() ?? "").Trim() : "";

    private static double Round(double seconds) => Math.Round(seconds, 3, MidpointRounding.AwayFromZero);
}
