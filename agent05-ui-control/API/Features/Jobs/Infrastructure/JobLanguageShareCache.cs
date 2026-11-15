using System.Collections.Concurrent;
using XtractManager.Features.Jobs.Application;

namespace XtractManager.Features.Jobs.Infrastructure;

/// <summary>
/// The share of a job (<see cref="JobLanguageShareReader.Read"/>), counted once and kept until a file it is
/// counted from changes. The lessons list shows the share of every run, and reading all chunk answers of every
/// job on each request would make the list slow; a look at the folder listing is cheap.
/// <para>
/// The files a share is counted from: <c>chunks_json/*.json</c>, <c>timeline/chunks.json</c>, and of an operator
/// split <c>split_chunks/chunk_N/chunk_N_merged.md</c> and <c>split_chunks/chunk_N/results/sub_chunk_*_result.json</c>.
/// Their number, total size and newest write time make the stamp; another stamp means the share is counted anew.
/// </para>
/// </summary>
public static class JobLanguageShareCache
{
    private static readonly ConcurrentDictionary<string, (string Stamp, LanguageShare? Share)> Entries =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Null when the folder has no chunk answer with a line in it. The caller gets a copy of its own.</summary>
    public static LanguageShare? Read(string jobDirectoryPath)
    {
        var stamp = StampOf(jobDirectoryPath);
        if (stamp == null)
            return null;

        var key = Path.GetFullPath(jobDirectoryPath);
        if (!Entries.TryGetValue(key, out var kept) || kept.Stamp != stamp)
        {
            kept = (stamp, JobLanguageShareReader.Read(jobDirectoryPath));
            Entries[key] = kept;
        }

        if (kept.Share == null)
            return null;
        var copy = new LanguageShare();
        copy.Add(kept.Share);
        return copy;
    }

    /// <summary>
    /// Counts the shares of many folders side by side, so that reading them one by one afterwards finds them kept.
    /// The first lessons list after a start has every job to count: one by one that took about 8 s for 160 jobs.
    /// </summary>
    public static void Warm(IEnumerable<string> jobDirectoryPaths)
    {
        try
        {
            Parallel.ForEach(
                jobDirectoryPaths,
                new ParallelOptions { MaxDegreeOfParallelism = Math.Clamp(Environment.ProcessorCount, 1, MaxWarmWorkers) },
                path => Read(path));
        }
        catch (AggregateException)
        {
            // A folder that fails here fails again, alone, where its share is asked for.
        }
    }

    private const int MaxWarmWorkers = 8;

    /// <summary>Null when the folder has no <c>chunks_json</c> or cannot be listed.</summary>
    internal static string? StampOf(string jobDirectoryPath)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(jobDirectoryPath))
                return null;
            var answers = new DirectoryInfo(Path.Combine(jobDirectoryPath, "chunks_json"));
            if (!answers.Exists)
                return null;

            long count = 0, bytes = 0, newest = 0;
            void See(FileInfo file)
            {
                count++;
                bytes += file.Length;
                newest = Math.Max(newest, file.LastWriteTimeUtc.Ticks);
            }

            foreach (var file in answers.EnumerateFiles("*.json"))
                See(file);

            var chunkPlaces = new FileInfo(Path.Combine(jobDirectoryPath, "timeline", "chunks.json"));
            if (chunkPlaces.Exists)
                See(chunkPlaces);

            var splits = new DirectoryInfo(Path.Combine(jobDirectoryPath, "split_chunks"));
            if (splits.Exists)
            {
                foreach (var chunkFolder in splits.EnumerateDirectories("chunk_*"))
                {
                    foreach (var merged in chunkFolder.EnumerateFiles("*_merged.md"))
                        See(merged);
                    var results = new DirectoryInfo(Path.Combine(chunkFolder.FullName, "results"));
                    if (!results.Exists)
                        continue;
                    foreach (var part in results.EnumerateFiles("sub_chunk_*_result.json"))
                        See(part);
                }
            }

            return FormattableString.Invariant($"{count}|{bytes}|{newest}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
