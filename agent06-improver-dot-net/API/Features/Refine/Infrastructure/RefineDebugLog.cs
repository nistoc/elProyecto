using System.Collections.Concurrent;
using System.Text;

namespace TranslationImprover.Features.Refine.Infrastructure;

/// <summary>
/// Best-effort append-only log under the job artifact root (read by Agent05 UI as refiner_debug.log).
/// </summary>
public static class RefineDebugLog
{
    public const string FileName = "refiner_debug.log";

    private static readonly ConcurrentDictionary<string, object> Locks = new(StringComparer.Ordinal);

    public static void Append(string? artifactRoot, string line)
    {
        if (string.IsNullOrEmpty(artifactRoot))
            return;
        try
        {
            var path = Path.Combine(artifactRoot, FileName);
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            var lockObj = Locks.GetOrAdd(path, _ => new object());
            lock (lockObj)
            {
                File.AppendAllText(path, $"[{DateTime.UtcNow:O}] {line}\n", Encoding.UTF8);
            }
        }
        catch
        {
            /* best-effort */
        }
    }

    /// <summary>
    /// Writes a timestamped header line then the full body in one write (for OpenAI error dumps).
    /// </summary>
    public static void AppendBlock(string? artifactRoot, string header, string body)
    {
        if (string.IsNullOrEmpty(artifactRoot))
            return;
        try
        {
            var path = Path.Combine(artifactRoot, FileName);
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            var lockObj = Locks.GetOrAdd(path, _ => new object());
            lock (lockObj)
            {
                var sb = new StringBuilder();
                sb.Append($"[{DateTime.UtcNow:O}] {header}\n");
                sb.Append(string.IsNullOrEmpty(body) ? "(empty body)" : body);
                if (sb.Length > 0 && sb[^1] != '\n')
                    sb.Append('\n');
                File.AppendAllText(path, sb.ToString(), Encoding.UTF8);
            }
        }
        catch
        {
            /* best-effort */
        }
    }
}
