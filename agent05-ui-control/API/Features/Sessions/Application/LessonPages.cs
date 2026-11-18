using System.Globalization;

namespace XtractManager.Features.Sessions.Application;

/// <summary>
/// A page made of a lesson outside this service — the lesson package with its vocabulary, topics, grammar
/// and new words. Only its address is kept here; the lesson shows it as a link.
/// </summary>
/// <param name="Url">Absolute http(s) address of the page.</param>
/// <param name="Title">What the link is called; null when no name was given (the UI then names it itself).</param>
/// <param name="AddedAt">UTC time the address was put, ISO 8601.</param>
public sealed record LessonPage(string Url, string? Title, string? AddedAt);

/// <summary>Addresses of the pages of the lessons, by the date of the lesson (<c>YYYY-MM-DD</c>).</summary>
public interface ILessonPagesStore
{
    /// <summary>
    /// Pages of every lesson that has one; of one lesson, the page put last comes first.
    /// Empty when nothing was put yet or the file cannot be read.
    /// </summary>
    IReadOnlyDictionary<string, IReadOnlyList<LessonPage>> Load();

    /// <summary>
    /// Puts the page first among the pages of the lesson; a page with this address that is already there
    /// keeps its place and only gets the new title. Returns the pages of the lesson.
    /// </summary>
    /// <exception cref="InvalidOperationException">The file cannot be read (it is left as it is), or the lesson has too many pages.</exception>
    /// <exception cref="IOException">The change could not be written; the file holds what it held before.</exception>
    IReadOnlyList<LessonPage> Put(string date, string url, string? title);

    /// <summary>
    /// Takes the page with this address off the lesson. Returns the pages left, or null when the lesson has
    /// no such page.
    /// </summary>
    /// <exception cref="InvalidOperationException">The file cannot be read; it is left as it is.</exception>
    /// <exception cref="IOException">The change could not be written; the file holds what it held before.</exception>
    IReadOnlyList<LessonPage>? Remove(string date, string url);
}

/// <summary>What a page of a lesson may be: checked when it is put and again when the file is read.</summary>
public static class LessonPageRules
{
    public const int MaxUrlLength = 2048;
    public const int MaxTitleLength = 120;
    public const int MaxPagesPerLesson = 20;

    /// <summary>A real day written <c>YYYY-MM-DD</c>, the way a lesson is dated.</summary>
    public static bool TryDate(string? raw, out string date)
    {
        date = (raw ?? "").Trim();
        return date.Length == 10
               && DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _);
    }

    /// <summary>
    /// An absolute http(s) address without a login in it. The link is opened by a click in the UI, so nothing
    /// else (<c>javascript:</c>, <c>file:</c>) may get into the list.
    /// </summary>
    public static bool TryUrl(string? raw, out string url, out string? error)
    {
        url = "";
        error = null;
        var text = (raw ?? "").Trim();
        if (text.Length == 0)
        {
            error = "url is required.";
            return false;
        }

        if (text.Length > MaxUrlLength)
        {
            error = $"url is longer than {MaxUrlLength} characters.";
            return false;
        }

        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)
            || string.IsNullOrEmpty(uri.Host))
        {
            error = "url must be an absolute http(s) address.";
            return false;
        }

        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            error = "url must not hold a login.";
            return false;
        }

        url = uri.AbsoluteUri;
        return true;
    }

    /// <summary>The name of the link: trimmed, null when blank.</summary>
    public static bool TryTitle(string? raw, out string? title, out string? error)
    {
        error = null;
        title = string.IsNullOrWhiteSpace(raw) ? null : raw.Trim();
        if (title is { Length: > MaxTitleLength })
        {
            error = $"title is longer than {MaxTitleLength} characters.";
            return false;
        }

        if (title != null && title.Any(char.IsControl))
        {
            error = "title must be one line of text.";
            return false;
        }

        return true;
    }
}
