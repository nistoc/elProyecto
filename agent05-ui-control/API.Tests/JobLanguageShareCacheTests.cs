using System.Text;
using XtractManager.Features.Jobs.Infrastructure;
using Xunit;

namespace XtractManager.Tests;

/// <summary>The share of a job is counted once and counted anew only when a file it is counted from changes.</summary>
public sealed class JobLanguageShareCacheTests : IDisposable
{
    private readonly string _workspace = Path.Combine(Path.GetTempPath(), "XtractManagerTests", "share_cache_" + Guid.NewGuid().ToString("N"));

    public JobLanguageShareCacheTests() => Directory.CreateDirectory(_workspace);

    public void Dispose()
    {
        try { Directory.Delete(_workspace, true); } catch { /* ignore */ }
    }

    private string JobDir(string jobId) => Path.Combine(_workspace, jobId);

    private string Put(string jobId, string relativePath, string content)
    {
        var full = Path.Combine(JobDir(jobId), relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content, new UTF8Encoding(false));
        return full;
    }

    private static string Answer(string text) =>
        "{\"segments\":[{\"start\":0,\"end\":10,\"text\":\"" + text + "\"}]}";

    // Two answers of the same size in bytes: nine bytes of Spanish, nine bytes of Russian.
    private const string Spanish = "vale vale";
    private const string Russian = "да да";

    [Fact]
    public void A_folder_without_chunk_answers_has_no_share()
    {
        Directory.CreateDirectory(JobDir("job1"));

        Assert.Null(JobLanguageShareCache.StampOf(JobDir("job1")));
        Assert.Null(JobLanguageShareCache.Read(JobDir("job1")));
        Assert.Null(JobLanguageShareCache.Read(JobDir("nowhere")));
        Assert.Null(JobLanguageShareCache.Read(""));
    }

    [Fact]
    public void The_share_is_the_one_the_reader_counts()
    {
        Put("job1", "chunks_json/audio_part_000.json", Answer("Hola, buenos días."));
        Put("job1", "chunks_json/audio_part_001.json", Answer("Привет, как дела?"));

        var share = JobLanguageShareCache.Read(JobDir("job1"))!;

        Assert.Equal(2, share.Lines);
        Assert.Equal(10, share.SpanishSeconds, 6);
        Assert.Equal(10, share.RussianSeconds, 6);
    }

    [Fact]
    public void An_unchanged_folder_is_not_read_again_and_a_changed_one_is()
    {
        Assert.Equal(Encoding.UTF8.GetByteCount(Spanish), Encoding.UTF8.GetByteCount(Russian));
        var file = Put("job1", "chunks_json/audio_part_000.json", Answer(Spanish));
        var written = File.GetLastWriteTimeUtc(file);

        var first = JobLanguageShareCache.Read(JobDir("job1"))!;
        Assert.Equal(1, first.SpanishLines);

        // Other words in a file of the same size and the same write time: the stamp is the same, so the kept
        // share comes back — which shows that the answers were not opened again.
        File.WriteAllText(file, Answer(Russian), new UTF8Encoding(false));
        File.SetLastWriteTimeUtc(file, written);
        var kept = JobLanguageShareCache.Read(JobDir("job1"))!;
        Assert.Equal(1, kept.SpanishLines);
        Assert.Equal(0, kept.RussianLines);

        // The file is written later: the share is counted anew.
        File.SetLastWriteTimeUtc(file, written.AddSeconds(5));
        var anew = JobLanguageShareCache.Read(JobDir("job1"))!;
        Assert.Equal(0, anew.SpanishLines);
        Assert.Equal(1, anew.RussianLines);
    }

    [Fact]
    public void A_new_answer_a_chunk_timeline_and_a_part_of_a_split_each_change_the_stamp()
    {
        Put("job1", "chunks_json/audio_part_000.json", Answer(Spanish));
        var alone = JobLanguageShareCache.StampOf(JobDir("job1"));

        Put("job1", "chunks_json/audio_part_001.json", Answer(Russian));
        var withSecondAnswer = JobLanguageShareCache.StampOf(JobDir("job1"));
        Assert.NotEqual(alone, withSecondAnswer);
        Assert.Equal(2, JobLanguageShareCache.Read(JobDir("job1"))!.Lines);

        Put("job1", "timeline/chunks.json", "{\"schema_version\":1,\"chunks\":[]}");
        var withTimeline = JobLanguageShareCache.StampOf(JobDir("job1"));
        Assert.NotEqual(withSecondAnswer, withTimeline);

        Put("job1", "split_chunks/chunk_0/chunk_0_merged.md", "- 0.00 speaker_0: \"vale\"");
        var withMerged = JobLanguageShareCache.StampOf(JobDir("job1"));
        Assert.NotEqual(withTimeline, withMerged);

        Put("job1", "split_chunks/chunk_0/results/sub_chunk_00_result.json", "{\"raw_response\":" + Answer(Russian) + "}");
        Assert.NotEqual(withMerged, JobLanguageShareCache.StampOf(JobDir("job1")));
        // Chunk 0 is now counted by its part: both lines are Russian.
        Assert.Equal(2, JobLanguageShareCache.Read(JobDir("job1"))!.RussianLines);
    }

    [Fact]
    public void Shares_counted_side_by_side_are_the_ones_counted_one_by_one()
    {
        var folders = new List<string>();
        for (var i = 0; i < 12; i++)
        {
            var job = "job" + i;
            // Job i has i Spanish lines and one Russian line.
            for (var c = 0; c < i; c++)
                Put(job, $"chunks_json/audio_part_{c:000}.json", Answer(Spanish));
            Put(job, "chunks_json/audio_part_999.json", Answer(Russian));
            folders.Add(JobDir(job));
        }

        folders.Add(JobDir("nowhere"));
        JobLanguageShareCache.Warm(folders);

        for (var i = 0; i < 12; i++)
        {
            var share = JobLanguageShareCache.Read(JobDir("job" + i))!;
            Assert.Equal(i, share.SpanishLines);
            Assert.Equal(1, share.RussianLines);
        }
    }

    [Fact]
    public void What_the_caller_does_to_the_share_it_got_does_not_reach_the_kept_one()
    {
        Put("job1", "chunks_json/audio_part_000.json", Answer(Spanish));

        var first = JobLanguageShareCache.Read(JobDir("job1"))!;
        first.Add("да да", 100);

        var second = JobLanguageShareCache.Read(JobDir("job1"))!;
        Assert.Equal(1, second.Lines);
        Assert.Equal(0, second.RussianLines);
    }
}
