using System.Security.Cryptography;
using System.Text;

namespace XtractManager.Features.LessonPackages.Application;

/// <summary>One item of a package the owner may edit: which list it is in and how its fields are read and written.</summary>
public sealed class LessonItemAccess
{
    private readonly Func<string, string?> _get;
    private readonly Action<string, string> _set;

    internal LessonItemAccess(
        LessonItem item,
        string list,
        IReadOnlyList<string> fields,
        Func<string, string?> get,
        Action<string, string> set)
    {
        Item = item;
        List = list;
        Fields = fields;
        _get = get;
        _set = set;
    }

    public LessonItem Item { get; }

    /// <summary>Name of the list in the package file: <c>new_words</c>, <c>grammar</c> and so on.</summary>
    public string List { get; }

    /// <summary>The fields that may be written anew; the first one names the item and cannot be left empty.</summary>
    public IReadOnlyList<string> Fields { get; }

    public string KeyField => Fields[0];

    public string? Get(string field) => _get(field);

    public void Set(string field, string text) => _set(field, text);
}

/// <summary>Gives every item of a package its id, puts the owner's edits on and counts the words against the earlier lessons.</summary>
public static class LessonPackageViewBuilder
{
    public const int MaxFieldLength = 2000;
    public const int MaxIdLength = 160;

    /// <summary>
    /// Gives every item its id and tells how each may be edited. The id is made of the builder's own text
    /// (a word — of the word, another item — of a digest of its text), never of the place in a list, so it
    /// stays the same while that text does, whatever the order of the lists. Two items of one name are told
    /// apart by a digest of the rest of their text; only items the same in everything get <c>-2</c>,
    /// <c>-3</c>.
    /// </summary>
    /// <param name="package">A package just read, with no edits put on yet.</param>
    public static IReadOnlyDictionary<string, LessonItemAccess> Items(LessonPackageDocument package)
    {
        var items = new Dictionary<string, LessonItemAccess>(StringComparer.Ordinal);
        var waiting = new List<(LessonItem Item, string Id, string Tie, LessonItemAccess Access)>();

        void Add(LessonItem item, string id, string tie, string list, string[] fields, Func<string, string?> get, Action<string, string> set) =>
            waiting.Add((item, id, tie, new LessonItemAccess(item, list, fields, get, set)));

        // The ids are given when all the items are known: only then is a name met twice seen.
        void Settle()
        {
            var met = waiting.GroupBy(w => w.Id, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
            foreach (var w in waiting)
            {
                var id = met[w.Id] > 1 ? $"{w.Id}-{w.Tie}" : w.Id;
                var unique = id;
                for (var n = 2; items.ContainsKey(unique); n++)
                    unique = $"{id}-{n}";
                w.Item.Id = unique;
                w.Item.Edited = null;
                w.Item.LeftOut = false;
                w.Item.EditedAt = null;
                items[unique] = w.Access;
            }

            waiting.Clear();
        }

        void AddWord(LessonWord w, string prefix, string list) => Add(
            w, prefix + LessonWordRules.Slug(w.Es), Tie(w.Ru, w.Period.ToString(), w.Line?.ToString(), w.Quote),
            list, new[] { "es", "ru", "note", "quote" },
            f => f switch { "es" => w.Es, "ru" => w.Ru, "note" => w.Note, _ => w.Quote },
            (f, v) =>
            {
                switch (f)
                {
                    case "es": w.Es = v; break;
                    case "ru": w.Ru = v; break;
                    case "note": w.Note = v; break;
                    default: w.Quote = v; break;
                }
            });

        void AddPlace(LessonPlace p, string id, string list) => Add(
            p, id, Tie(p.At, p.Who, p.Said), list, new[] { "quote" }, _ => p.Quote, (_, v) => p.Quote = v);

        void AddGrammar(LessonGrammar g, string prefix, string list) => Add(
            g, prefix + Digest(g.Topic, g.Rule, g.Period.ToString()), Tie(g.Theme, g.Kind, g.Points.ToString()),
            list, new[] { "topic", "rule" },
            f => f == "topic" ? g.Topic : g.Rule,
            (f, v) =>
            {
                if (f == "topic") g.Topic = v;
                else g.Rule = v;
            });

        // An example of a theme is known by its own text too: the builder may give the examples in another order.
        void AddGrammarExamples(LessonGrammar g, string list)
        {
            foreach (var example in g.Examples)
            {
                AddPlace(
                    example, $"{g.Id}-e-{Digest(example.Quote, example.Period.ToString(), example.Line?.ToString())}", list);
            }

            foreach (var part in g.Parts)
            foreach (var example in part.Examples)
            {
                AddPlace(
                    example,
                    $"{g.Id}-p-{Digest(part.Topic, part.Rule, example.Quote, example.Period.ToString(), example.Line?.ToString())}",
                    list);
            }
        }

        foreach (var t in package.Topics)
        {
            Add(
                t, "t-" + Digest(t.Title, t.Period.ToString(), t.FromLine?.ToString()), Tie(t.Summary, t.ToLine?.ToString()),
                "topics", new[] { "title", "summary" },
                f => f == "title" ? t.Title : t.Summary,
                (f, v) =>
                {
                    if (f == "title") t.Title = v;
                    else t.Summary = v;
                });
        }

        foreach (var g in package.Grammar)
            AddGrammar(g, "g-", "grammar");
        foreach (var g in package.WordNotes)
            AddGrammar(g, "w-", "word_notes");

        foreach (var e in package.Examples)
        {
            Add(
                e, "e-" + Digest(e.Quote, e.Period.ToString(), e.Line?.ToString()), Tie(e.Ru, e.Shows),
                "examples", new[] { "quote", "ru", "shows" },
                f => f switch { "quote" => e.Quote, "ru" => e.Ru, _ => e.Shows },
                (f, v) =>
                {
                    switch (f)
                    {
                        case "quote": e.Quote = v; break;
                        case "ru": e.Ru = v; break;
                        default: e.Shows = v; break;
                    }
                });
        }

        foreach (var w in package.NewWords)
            AddWord(w, "n-", "new_words");
        foreach (var w in package.Vocabulary)
            AddWord(w, "v-", "vocabulary");

        foreach (var w in package.OtherWords)
        {
            Add(
                w, "o-" + LessonWordRules.Slug(w.Raw ?? w.Word), Tie(w.Word, w.Period.ToString(), w.At, w.Said),
                "other_words", new[] { "raw", "ru", "lemma", "said" },
                f => f switch { "raw" => w.Raw, "ru" => w.Ru, "lemma" => w.Lemma, _ => w.Said },
                (f, v) =>
                {
                    switch (f)
                    {
                        case "raw": w.Raw = v; break;
                        case "ru": w.Ru = v; break;
                        case "lemma": w.Lemma = v; break;
                        default: w.Said = v; break;
                    }
                });
        }

        Settle();

        // The examples inside the themes come after: their ids begin with the id of their theme.
        foreach (var g in package.Grammar)
            AddGrammarExamples(g, "grammar");
        foreach (var g in package.WordNotes)
            AddGrammarExamples(g, "word_notes");
        Settle();

        return items;
    }

    /// <param name="package">A package just read: it is changed in place.</param>
    /// <param name="marks">The owner's edits by item id.</param>
    /// <param name="lessons">The words of the lessons, oldest first; only the ones before the package's date are used.</param>
    /// <param name="mixedTrackByJob">For the job of a period — the mixed track of its recording, when it has one.</param>
    /// <param name="hearingsByPeriod">The hearings of the recording of a period, by its number: whether a word was
    /// heard elsewhere is told only for the periods given (<see cref="HeardElsewhere"/>).</param>
    public static LessonPackageView Build(
        string date,
        LessonPackageDocument package,
        IReadOnlyDictionary<string, LessonMark> marks,
        IReadOnlyList<LessonWords> lessons,
        IReadOnlyDictionary<string, string>? mixedTrackByJob = null,
        IReadOnlyDictionary<int, LessonHearing>? hearingsByPeriod = null)
    {
        var items = Items(package);
        var edited = 0;
        var leftOut = 0;
        var stale = 0;
        foreach (var (id, mark) in marks)
        {
            if (!items.TryGetValue(id, out var item))
            {
                stale++;
                continue;
            }

            item.Item.LeftOut = mark.LeftOut;
            item.Item.EditedAt = mark.At;
            foreach (var (field, text) in mark.Fields ?? new Dictionary<string, string>())
            {
                if (text == null || !item.Fields.Contains(field))
                    continue;
                var built = item.Get(field);
                if (string.Equals(built ?? "", text, StringComparison.Ordinal))
                    continue;
                (item.Item.Edited ??= new Dictionary<string, string?>(StringComparer.Ordinal))[field] = built;
                item.Set(field, text);
            }

            if (item.Item.Edited != null)
                edited++;
            if (mark.LeftOut)
                leftOut++;
        }

        // The words are looked for as the owner wrote them: a word written anew is counted anew.
        var earlier = lessons.Where(l => string.CompareOrdinal(l.Date, date) < 0).ToList();
        foreach (var word in package.NewWords.Concat(package.Vocabulary))
            word.SeenIn = LessonWordRules.SeenBefore(word.Es, word.Quote, earlier);
        foreach (var word in package.OtherWords)
            word.SeenIn = LessonWordRules.KnownIn(word.Edited?.ContainsKey("raw") == true ? word.Raw : word.Word ?? word.Raw, earlier);

        // A word the owner wrote anew is the owner's: the hearings are not asked about it.
        if (hearingsByPeriod != null)
        {
            foreach (var word in package.NewWords.Concat(package.Vocabulary))
            {
                word.HeardElsewhere = hearingsByPeriod.TryGetValue(word.Period, out var hearing)
                                      && word.Edited?.ContainsKey("es") != true
                    ? HeardElsewhere.Check(hearing, string.IsNullOrWhiteSpace(word.Heard) ? word.Es : word.Heard, word.Sec, word.Said, word.Quote)
                    : null;
            }

            foreach (var word in package.OtherWords)
            {
                word.HeardElsewhere = hearingsByPeriod.TryGetValue(word.Period, out var hearing)
                                      && word.Edited?.ContainsKey("raw") != true
                    ? HeardElsewhere.Check(hearing, word.Raw ?? word.Word, word.Sec, word.Said)
                    : null;
            }
        }

        foreach (var period in package.Periods)
        {
            period.AudioTrack = period.Job != null && mixedTrackByJob != null && mixedTrackByJob.TryGetValue(period.Job, out var track)
                ? track
                : null;
        }

        var counts = new LessonPackageCounts(
            NewWords: package.NewWords.Count(w => !w.LeftOut),
            NewWordsNeverSeen: package.NewWords.Count(w => !w.LeftOut && w.SeenIn is { Count: 0 }),
            Vocabulary: package.Vocabulary.Count(w => !w.LeftOut),
            VocabularyNeverSeen: package.Vocabulary.Count(w => !w.LeftOut && w.SeenIn is { Count: 0 }),
            OtherWords: package.OtherWords.Count(w => !w.LeftOut),
            OtherWordsNeverSeen: package.OtherWords.Count(w => !w.LeftOut && w.SeenIn.Count == 0),
            Edited: edited,
            LeftOut: leftOut,
            Stale: stale,
            NotHeardElsewhere: package.NewWords.Concat(package.Vocabulary).Count(w => !w.LeftOut && w.HeardElsewhere == false)
                               + package.OtherWords.Count(w => !w.LeftOut && w.HeardElsewhere == false));
        var compared = new LessonsCompared(
            earlier.Count,
            earlier.FirstOrDefault()?.Date,
            earlier.LastOrDefault()?.Date,
            earlier.Select(l => l.Date).ToList());
        return new LessonPackageView(date, package, compared, counts);
    }

    /// <summary>
    /// The edit an item keeps after a change. A field given as null goes back to the builder's text, and so
    /// does one given the very text the builder wrote; <paramref name="leftOut"/> null leaves that as it was.
    /// </summary>
    /// <returns>The edit to keep; null when nothing of it is left.</returns>
    /// <exception cref="ArgumentException">A field the item does not have, a text too long, or the name of the item left empty.</exception>
    public static LessonMark? Changed(
        LessonItemAccess item,
        LessonMark? known,
        IReadOnlyDictionary<string, string?>? fields,
        bool? leftOut,
        string at)
    {
        var mark = new LessonMark
        {
            Fields = known?.Fields != null ? new Dictionary<string, string>(known.Fields, StringComparer.Ordinal) : new(StringComparer.Ordinal),
            Was = known?.Was != null ? new Dictionary<string, string?>(known.Was, StringComparer.Ordinal) : new(StringComparer.Ordinal),
            LeftOut = leftOut ?? known?.LeftOut ?? false,
            Other = known?.Other,
        };
        foreach (var (field, raw) in fields ?? new Dictionary<string, string?>())
        {
            if (!item.Fields.Contains(field))
                throw new ArgumentException($"An item of {item.List} has no field '{field}' to edit (it has: {string.Join(", ", item.Fields)}).");
            var built = item.Get(field);
            var text = raw == null ? null : OneSpaced(raw);
            if (text == null || string.Equals(text, built ?? "", StringComparison.Ordinal))
            {
                mark.Fields.Remove(field);
                mark.Was.Remove(field);
                continue;
            }

            if (text.Length > MaxFieldLength)
                throw new ArgumentException($"{field} is longer than {MaxFieldLength} characters.");
            if (text.Length == 0 && field == item.KeyField)
                throw new ArgumentException($"{field} cannot be left empty; to hide the item, take it out.");
            mark.Fields[field] = text;
            mark.Was[field] = built;
        }

        // An edit of a field the item no longer has is of no use to anyone.
        foreach (var gone in mark.Fields.Keys.Where(f => !item.Fields.Contains(f)).ToList())
        {
            mark.Fields.Remove(gone);
            mark.Was.Remove(gone);
        }

        mark.Of = $"{item.List}: {Short(item.Get(item.KeyField))}";
        // No edit is left: what a hand added to the entry stays in the file, and nothing else of it.
        if (!mark.HasEdit)
            return mark.WithoutEdit();
        if (mark.Fields.Count == 0)
        {
            mark.Fields = null;
            mark.Was = null;
        }

        mark.At = at;
        return mark;
    }

    /// <summary>The id an edit is kept by: small Latin letters, digits and dashes.</summary>
    public static bool TryMarkId(string? raw, out string id)
    {
        id = raw?.Trim() ?? "";
        return id.Length is > 0 and <= MaxIdLength && id.All(c => c is >= 'a' and <= 'z' or >= '0' and <= '9' or '-');
    }

    /// <summary>Ten hex digits of a digest of the texts: enough to tell the items of one lesson apart.</summary>
    private static string Digest(params string?[] parts)
    {
        var hash = SHA1.HashData(Encoding.UTF8.GetBytes(string.Join('\u001f', parts.Select(p => p ?? ""))));
        return Convert.ToHexString(hash, 0, 5).ToLowerInvariant();
    }

    /// <summary>Six hex digits of the rest of an item's text: what tells two items of one name apart.</summary>
    private static string Tie(params string?[] parts) => Digest(parts)[..6];

    /// <summary>One line: every run of spaces, tabs and line breaks is one space.</summary>
    private static string OneSpaced(string text)
    {
        var printable = new string(text.Select(c => char.IsControl(c) ? ' ' : c).ToArray());
        return string.Join(' ', printable.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    private static string Short(string? text)
    {
        var t = text ?? "";
        return t.Length <= 60 ? t : t[..60] + "…";
    }
}
