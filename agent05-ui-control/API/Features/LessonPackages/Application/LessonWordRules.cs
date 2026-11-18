using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace XtractManager.Features.LessonPackages.Application;

/// <summary>
/// The Latin-letter words of one lesson, read from its transcripts: what a later lesson is compared with
/// to tell which of its words were said before.
/// </summary>
public sealed class LessonWords
{
    /// <param name="date">Date of the lesson, <c>YYYY-MM-DD</c>.</param>
    /// <param name="lines">The text of every line of the lesson's transcripts.</param>
    public LessonWords(string date, IEnumerable<string> lines)
    {
        Date = date;
        var forms = new HashSet<string>(StringComparer.Ordinal);
        var text = new StringBuilder(" ");
        foreach (var line in lines)
        {
            var words = LessonWordRules.LatinWords(line);
            if (words.Count == 0)
                continue;
            forms.UnionWith(words);
            // A line break stands between two lines, so a phrase is never found across them.
            text.Append(string.Join(' ', words)).Append(" \n ");
        }

        var prefixes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var form in forms)
            for (var k = 3; k <= form.Length; k++)
                prefixes.Add(form[..k]);

        Forms = forms;
        Prefixes = prefixes;
        Text = text.ToString();
    }

    public string Date { get; }

    /// <summary>Every word form of the lesson: lower case, no accents.</summary>
    public IReadOnlySet<string> Forms { get; }

    /// <summary>Every beginning of a form from three letters on.</summary>
    public IReadOnlySet<string> Prefixes { get; }

    /// <summary>The words of every line, a space between words and a line break between lines.</summary>
    public string Text { get; }
}

/// <summary>
/// How a word of a lesson is compared with the earlier lessons. The rules are by code and the same for
/// every lesson, so the answer changes only when the transcripts do: when an earlier lesson is transcribed,
/// the words of the later ones are counted against it at once.
/// </summary>
public static class LessonWordRules
{
    private static readonly Regex Latin = new("[A-Za-zÁÉÍÓÚÜÑáéíóúüñ]+", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex InBrackets = new(@"\([^)]*\)", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex NotSlug = new("[^a-z0-9]+", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>Words too small to tell one entry from another.</summary>
    private static readonly HashSet<string> Small = new(
        "de la el un una unos unas a en con por para que se lo los las del al y o e u no si su mi tu me te le nos es".Split(' '),
        StringComparer.Ordinal);

    /// <summary>Words that stand in an entry for "any word here" (llevar + gerundio).</summary>
    private static readonly HashSet<string> Placeholders = new(
        "gerundio infinitivo participio algo alguien sustantivo adjetivo verbo tiempo".Split(' '),
        StringComparer.Ordinal);

    /// <summary>Lower case, no accents: «Está» — "esta". For matching one spelling of a word against another.</summary>
    public static string Plain(string? s)
    {
        if (string.IsNullOrEmpty(s))
            return "";
        var decomposed = s.ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                sb.Append(c);
        return sb.ToString();
    }

    /// <summary>The Latin-letter words of a text, each lower case and without accents.</summary>
    public static IReadOnlyList<string> LatinWords(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return Array.Empty<string>();
        return Latin.Matches(text).Select(m => Plain(m.Value)).ToList();
    }

    /// <summary>The id of the mark of a word: the word in plain letters, everything else a dash.</summary>
    public static string Slug(string? word)
    {
        var slug = NotSlug.Replace(Plain(word), "-").Trim('-');
        if (slug.Length == 0)
            return "x";
        return slug.Length > 120 ? slug[..120] : slug;
    }

    /// <summary>
    /// Was this form, or a form of the same word, said in the lesson? The same word: the beginning is the
    /// same but for the last two letters (habla — hablo — hablamos), and never less than four letters of it.
    /// </summary>
    public static bool KnownForm(string form, LessonWords lesson)
    {
        if (lesson.Forms.Contains(form))
            return true;
        return form.Length >= 5 && lesson.Prefixes.Contains(form[..Math.Max(4, form.Length - 2)]);
    }

    /// <summary>
    /// The dates of the lessons that have the entry, in the order given. Null when the entry is a pattern
    /// with no word of its own to look for.
    /// </summary>
    /// <param name="entry">The word or phrase as the list has it; what stands in brackets is not looked for.</param>
    /// <param name="quote">The line the entry was taken from: a form of the word said there counts too.</param>
    public static IReadOnlyList<string>? SeenBefore(string? entry, string? quote, IReadOnlyList<LessonWords> earlier)
    {
        var es = entry ?? "";
        var words = LatinWords(InBrackets.Replace(es, " "));
        var content = words.Where(w => !Small.Contains(w) && !Placeholders.Contains(w) && w.Length >= 3).ToList();
        if (content.Count == 0)
            return null;

        var dates = new List<string>();
        if (content.Count == 1)
        {
            var word = content[0];
            var forms = new List<string> { word };
            forms.AddRange(LatinWords(quote).Where(q => q.Length >= 4 && word.Length >= 4 && q[..4] == word[..4]));
            var stem = VerbStem(word);
            foreach (var lesson in earlier)
                if (forms.Any(f => KnownForm(f, lesson)) || (stem != null && lesson.Prefixes.Contains(stem)))
                    dates.Add(lesson.Date);
            return dates;
        }

        if (es.Contains('+'))
        {
            // A pattern (llevar + gerundio): its first word.
            foreach (var lesson in earlier)
                if (KnownForm(content[0], lesson))
                    dates.Add(lesson.Date);
            return dates;
        }

        var phrase = " " + string.Join(' ', words.Where(w => !Placeholders.Contains(w))) + " ";
        foreach (var lesson in earlier)
            if (lesson.Text.Contains(phrase, StringComparison.Ordinal))
                dates.Add(lesson.Date);
        return dates;
    }

    /// <summary>
    /// The lessons that have this word form (or a form of the same word), in the order given. For a word
    /// found by code, where there is no entry and no quote.
    /// </summary>
    public static IReadOnlyList<string> KnownIn(string? form, IReadOnlyList<LessonWords> earlier)
    {
        var plain = Plain(form);
        if (plain.Length == 0)
            return Array.Empty<string>();
        return earlier.Where(lesson => KnownForm(plain, lesson)).Select(lesson => lesson.Date).ToList();
    }

    /// <summary>A long infinitive without its ending (trabajar — trabaj, quedarse — qued); null for anything else.</summary>
    private static string? VerbStem(string word)
    {
        var stem = word.EndsWith("se", StringComparison.Ordinal) ? word[..^2] : word;
        var infinitive = stem.EndsWith("ar", StringComparison.Ordinal)
                         || stem.EndsWith("er", StringComparison.Ordinal)
                         || stem.EndsWith("ir", StringComparison.Ordinal);
        return infinitive && stem.Length >= 6 ? stem[..^2] : null;
    }
}
