using System.Text.RegularExpressions;

namespace XtractManager.Features.LessonText.Application;

/// <summary>
/// A guess of the teacher and the student of a recording from how much they say: of the two speakers who
/// say most, the one with more words is the teacher. Measured 2026-11-20 on the 33 recordings whose
/// participant tracks are labelled by role: more words told the teacher in 33 of 33 (the smallest lead was
/// 1.19 times), a larger share of Spanish in 29, more lines in 28, more question marks per line in 27.
/// No guess is made when the two say about as much (<see cref="MinWordRatio"/>) or when the second says too
/// little to be one of them (<see cref="MinWordShare"/>); a speaker who says little beside the two (often
/// the same person the other service split off) gets no role.
/// </summary>
public static class LessonRoleGuess
{
    /// <summary>Of all words of the recording, the second speaker must say at least this share to be a participant.</summary>
    public const double MinWordShare = 0.1;

    /// <summary>The teacher must say at least this many times the words of the student.</summary>
    public const double MinWordRatio = 1.15;

    private static readonly Regex Word = new(@"\p{L}+", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex Latin = new("^[A-Za-zÁÉÍÓÚÜÑáéíóúüñ]+$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Dictionary<string, string> RoleWords = new(StringComparer.OrdinalIgnoreCase)
    {
        ["преподаватель"] = LessonRole.Teacher,
        ["учитель"] = LessonRole.Teacher,
        ["teacher"] = LessonRole.Teacher,
        ["profesor"] = LessonRole.Teacher,
        ["profesora"] = LessonRole.Teacher,
        ["ученик"] = LessonRole.Student,
        ["ученица"] = LessonRole.Student,
        ["student"] = LessonRole.Student,
        ["alumno"] = LessonRole.Student,
        ["alumna"] = LessonRole.Student,
    };

    /// <summary>The role a speaker's name says, when the track was labelled by role («Преподаватель», «Ученик»).</summary>
    public static string? RoleNamed(string? speaker) =>
        speaker != null && RoleWords.TryGetValue(speaker.Trim(), out var role) ? role : null;

    /// <summary>Words of a line and how many of them are written in Latin letters.</summary>
    public static (int Words, int Latin) Count(string text)
    {
        int words = 0, latin = 0;
        foreach (Match m in Word.Matches(text ?? ""))
        {
            words++;
            if (Latin.IsMatch(m.Value))
                latin++;
        }

        return (words, latin);
    }

    /// <param name="wordsBySpeaker">Words said by every speaker of one recording.</param>
    /// <returns>The guessed role by speaker; a speaker without a guess is absent.</returns>
    public static IReadOnlyDictionary<string, string> Guess(IReadOnlyDictionary<string, int> wordsBySpeaker)
    {
        var guess = new Dictionary<string, string>(StringComparer.Ordinal);
        var total = wordsBySpeaker.Values.Sum();
        var ranked = wordsBySpeaker
            .Where(s => s.Value > 0)
            .OrderByDescending(s => s.Value)
            .ThenBy(s => s.Key, StringComparer.Ordinal)
            .ToList();
        if (total == 0 || ranked.Count < 2 || ranked[1].Value < MinWordShare * total
            || ranked[0].Value < MinWordRatio * ranked[1].Value)
            return guess;

        guess[ranked[0].Key] = LessonRole.Teacher;
        guess[ranked[1].Key] = LessonRole.Student;
        return guess;
    }
}
