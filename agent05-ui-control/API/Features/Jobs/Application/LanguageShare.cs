namespace XtractManager.Features.Jobs.Application;

/// <summary>What a line of a transcript is written in, told by the letters of its words.</summary>
public enum LineLanguage
{
    /// <summary>No word with a letter: a number, a dash.</summary>
    None,
    Spanish,
    Russian,
    Mixed,
}

/// <summary>
/// How much of the speech of a transcript is Spanish and how much Russian. The script tells the language:
/// a word of Latin letters counts as Spanish, a word with a Cyrillic letter as Russian (the lessons have no
/// third language; an English word would count as Spanish). A line is Spanish when at least
/// <see cref="SpanishFromLatinShare"/> of its words are Latin, Russian when at most
/// <see cref="RussianUpToLatinShare"/> are, mixed otherwise. The seconds of a line are those of its segment.
/// </summary>
public sealed class LanguageShare
{
    public const double SpanishFromLatinShare = 0.7;
    public const double RussianUpToLatinShare = 0.3;

    /// <summary>Lines with a word in them, timed or not.</summary>
    public int Lines { get; set; }

    public int SpanishLines { get; set; }
    public int RussianLines { get; set; }
    public int MixedLines { get; set; }

    /// <summary>Lines of an answer that came without time marks: they are in the line counts, not in the seconds.</summary>
    public int UntimedLines { get; set; }

    /// <summary>Seconds of all timed lines: what was spoken, pauses between the lines left out.</summary>
    public double SpeechSeconds { get; set; }

    public double SpanishSeconds { get; set; }
    public double RussianSeconds { get; set; }
    public double MixedSeconds { get; set; }

    /// <summary>The language of a line by the rule of this class.</summary>
    public static LineLanguage Classify(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return LineLanguage.None;

        int latin = 0, cyrillic = 0;
        foreach (var word in text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            bool hasLatin = false, hasCyrillic = false;
            foreach (var ch in word)
            {
                if (ch >= 'Ѐ' && ch <= 'ӿ')
                    hasCyrillic = true;
                else if (ch < 'ɐ' && char.IsLetter(ch))
                    hasLatin = true;
            }

            // A word with both scripts is a Russian word with a stray Latin letter more often than the reverse.
            if (hasCyrillic)
                cyrillic++;
            else if (hasLatin)
                latin++;
        }

        var words = latin + cyrillic;
        if (words == 0)
            return LineLanguage.None;
        var latinShare = (double)latin / words;
        if (latinShare >= SpanishFromLatinShare)
            return LineLanguage.Spanish;
        return latinShare <= RussianUpToLatinShare ? LineLanguage.Russian : LineLanguage.Mixed;
    }

    /// <summary>Counts one line; <paramref name="seconds"/> is null for a line without time marks.</summary>
    public void Add(string? text, double? seconds)
    {
        var language = Classify(text);
        if (language == LineLanguage.None)
            return;

        Lines++;
        var sec = seconds is { } s && s > 0 && !double.IsInfinity(s) ? s : 0;
        if (seconds == null)
            UntimedLines++;
        SpeechSeconds += sec;
        switch (language)
        {
            case LineLanguage.Spanish:
                SpanishLines++;
                SpanishSeconds += sec;
                break;
            case LineLanguage.Russian:
                RussianLines++;
                RussianSeconds += sec;
                break;
            default:
                MixedLines++;
                MixedSeconds += sec;
                break;
        }
    }

    /// <summary>Adds the counts of another transcript (tracks of a dialogue into its total).</summary>
    public void Add(LanguageShare other)
    {
        Lines += other.Lines;
        SpanishLines += other.SpanishLines;
        RussianLines += other.RussianLines;
        MixedLines += other.MixedLines;
        UntimedLines += other.UntimedLines;
        SpeechSeconds += other.SpeechSeconds;
        SpanishSeconds += other.SpanishSeconds;
        RussianSeconds += other.RussianSeconds;
        MixedSeconds += other.MixedSeconds;
    }
}

/// <summary>
/// The answer of <c>GET /api/jobs/{id}/language-share</c>. A transcription job has <see cref="Total"/> only;
/// a dialogue job has one entry per track (who spoke how much and in what language), their sum, and
/// <see cref="Conversation"/>.
/// </summary>
public sealed class JobLanguageShareReport
{
    /// <summary>Null when the job has no line to count yet.</summary>
    public LanguageShare? Total { get; set; }

    public List<TrackLanguageShare> Tracks { get; set; } = new();

    /// <summary>
    /// How the talk of a dialogue goes: overlaps, interruptions, pauses. Null for a transcription job and for a
    /// dialogue whose tracks cannot all be put on the timeline of the recording.
    /// </summary>
    public ConversationReport? Conversation { get; set; }
}

public sealed class TrackLanguageShare
{
    public string JobId { get; set; } = "";
    public string Speaker { get; set; } = "";

    /// <summary>Null when the job of the track is gone or has no line to count.</summary>
    public LanguageShare? Share { get; set; }
}
