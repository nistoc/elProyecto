using TranslationImprover.Features.Refine.Domain;

namespace TranslationImprover.Features.Refine.Infrastructure;

/// <summary>
/// Parses transcript structure (>>>>>>>/<<<<<) and creates batches. Public for unit testing.
/// </summary>
public static class TranscriptParser
{
    public static void ParseStructure(IReadOnlyList<string> lines, out IReadOnlyList<string> header, out IReadOnlyList<string> content, out IReadOnlyList<string> footer)
    {
        int? contentStart = null, contentEnd = null;
        for (var i = 0; i < lines.Count; i++)
        {
            if (lines[i].Trim() == ">>>>>>>")
                contentStart = i + 1;
            else if (lines[i].Trim() == "<<<<<")
            {
                contentEnd = i;
                break;
            }
        }
        if (contentStart == null || contentEnd == null)
        {
            header = Array.Empty<string>();
            content = lines;
            footer = Array.Empty<string>();
            return;
        }
        header = lines.Take(contentStart.Value).ToList();
        content = lines.Skip(contentStart.Value).Take(contentEnd.Value - contentStart.Value).ToList();
        footer = lines.Skip(contentEnd.Value).ToList();
    }

    public static List<BatchInfo> CreateBatches(IReadOnlyList<string> lines, int batchSize)
    {
        var batches = new List<BatchInfo>();
        for (var i = 0; i < lines.Count; i += batchSize)
        {
            var chunk = lines.Skip(i).Take(batchSize).ToList();
            batches.Add(new BatchInfo
            {
                Index = batches.Count,
                StartLine = i,
                EndLine = i + chunk.Count,
                Lines = chunk
            });
        }
        return batches;
    }
}
