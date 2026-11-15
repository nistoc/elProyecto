using System.Text;
using System.Text.RegularExpressions;

namespace TranslationImprover.Features.Refine.Infrastructure;

/// <summary>The words of a transcript line and the alphabet they are written in.</summary>
public static class TranscriptWords
{
    // - 12.34 speaker_0: "text"   (the speaker may be a participant's name)
    public static readonly Regex Line = new(
        "^(?<head>\\s*-\\s+\\S+\\s+[^:\"]+:\\s*\")(?<text>.*)(?<tail>\"\\s*)$",
        RegexOptions.CultureInvariant | RegexOptions.Singleline);

    /// <summary>What was said in the line: the text between the quotes, or the whole line when it has another form.</summary>
    public static string TextOf(string line)
    {
        var m = Line.Match(line);
        return m.Success ? m.Groups["text"].Value : line;
    }

    /// <summary>"- 12.50 speaker_0: \"…\"" — "12.50"; empty for a line of another form.</summary>
    public static string TimeMarkOf(string line)
    {
        var m = Line.Match(line);
        if (!m.Success)
            return "";
        var parts = m.Groups["head"].Value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return parts.Length > 1 ? parts[1] : "";
    }

    /// <summary>The line cut at white space; a token keeps the punctuation written next to its word.</summary>
    public static List<string> Tokens(string text) =>
        text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).ToList();

    public static bool HasLetter(string token) => token.Any(char.IsLetter);

    public static bool IsCyrillic(char c) => c is >= 'Ѐ' and <= 'ӿ';

    public static bool IsLatin(char c) => c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= 'À' and <= 'ɏ';

    public static bool HasCyrillic(string token) => token.Any(IsCyrillic);

    public static bool HasLatin(string token) => token.Any(IsLatin);

    /// <summary>A word of Latin letters only: Spanish as the transcript writes it.</summary>
    public static bool IsLatinWord(string token) => HasLatin(token) && !HasCyrillic(token);

    /// <summary>Lower-cased letters a–z of the word, accents dropped: the form the transcript's Spanish is kept in.</summary>
    public static string Bare(string token)
    {
        var sb = new StringBuilder(token.Length);
        foreach (var c in token.ToLowerInvariant().Normalize(NormalizationForm.FormD))
        {
            if (c is >= 'a' and <= 'z')
                sb.Append(c);
        }

        return sb.ToString();
    }

    /// <summary>The letters of a token, lower-cased: «Но,» — "но".</summary>
    public static string LettersOf(string token) =>
        new(token.ToLowerInvariant().Where(char.IsLetter).ToArray());

    /// <summary>
    /// A token as the word and what is written around it: «¿Комо?» — "¿", "Комо", "?". The word runs from the
    /// first letter to the last, so a hyphen or an apostrophe inside it stays in it.
    /// </summary>
    public static (string Lead, string Word, string Trail) Parts(string token)
    {
        int first = 0, last = token.Length - 1;
        while (first < token.Length && !char.IsLetter(token[first]))
            first++;
        while (last >= first && !char.IsLetter(token[last]))
            last--;
        return first > last
            ? (token, "", "")
            : (token[..first], token[first..(last + 1)], token[(last + 1)..]);
    }
}
