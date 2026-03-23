using System.Linq;
using XtractManager.Features.Jobs.Application;

namespace XtractManager.Features.Jobs.Infrastructure;

/// <summary>Merges Agent06 refine batch payloads into <see cref="JobSnapshot.RefinerThreadBatches"/>.</summary>
public static class RefinerThreadBatchMerge
{
    public static void Apply(JobSnapshot snapshot, RefineStatusUpdate update)
    {
        if (string.IsNullOrEmpty(update.BatchEventKind)
            && string.IsNullOrEmpty(update.BatchBeforeText)
            && string.IsNullOrEmpty(update.BatchAfterText))
            return;

        var idx = update.BatchEventIndex0;
        if (idx < 0)
            return;

        var total = update.TotalBatches;
        if (total <= 0 && snapshot.RefinerThreadBatches is { Count: > 0 })
            total = snapshot.RefinerThreadBatches.Max(x => x.TotalBatches);
        if (total <= 0)
            return;

        var list = snapshot.RefinerThreadBatches != null
            ? snapshot.RefinerThreadBatches.ToList()
            : new List<RefinerThreadBatchEntry>();

        var existing = list.Find(x => x.BatchIndex == idx && x.TotalBatches == total);
        if (existing == null)
        {
            existing = new RefinerThreadBatchEntry { BatchIndex = idx, TotalBatches = total };
            list.Add(existing);
        }

        if (!string.IsNullOrEmpty(update.BatchBeforeText))
            existing.BeforeText = update.BatchBeforeText;
        if (string.Equals(update.BatchEventKind, "output_ready", StringComparison.OrdinalIgnoreCase))
            existing.AfterText = update.BatchAfterText ?? "";
        // Do not clear AfterText on input_ready: a following input_ready for the *same* index should not wipe
        // a just-applied output_ready if events reorder; new rows already have AfterText null.

        list.Sort((a, b) => a.BatchIndex.CompareTo(b.BatchIndex));
        snapshot.RefinerThreadBatches = list;
    }
}
