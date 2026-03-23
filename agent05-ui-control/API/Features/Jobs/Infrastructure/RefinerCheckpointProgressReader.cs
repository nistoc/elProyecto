using System.Text.Json;

namespace XtractManager.Features.Jobs.Infrastructure;

/// <summary>
/// Reads <c>refiner_threads/checkpoint.json</c> (Agent06) to expose remaining batch work without a live gRPC job id.
/// </summary>
public static class RefinerCheckpointProgressReader
{
    public sealed record Summary(int NextBatchIndex0, int TotalBatches)
    {
        /// <summary>Batches not yet processed (from nextBatchIndex to totalBatches-1).</summary>
        public int RemainingBatches => TotalBatches > 0 ? Math.Max(0, TotalBatches - NextBatchIndex0) : 0;

        public bool CanResume => TotalBatches > 0 && NextBatchIndex0 < TotalBatches && NextBatchIndex0 >= 0;
    }

    public static bool TryRead(string jobDirectoryPath, out Summary? summary)
    {
        summary = null;
        var path = Path.Combine(jobDirectoryPath, "refiner_threads", "checkpoint.json");
        if (!File.Exists(path)) return false;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var root = doc.RootElement;
            if (!root.TryGetProperty("nextBatchIndex", out var nextEl) || !root.TryGetProperty("totalBatches", out var totalEl))
                return false;
            var next = nextEl.GetInt32();
            var total = totalEl.GetInt32();
            summary = new Summary(next, total);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
