using System.Text.Json;

namespace TranslationImprover.Features.Refine.Infrastructure;

/// <summary>One file per batch under <c>refiner_threads/batch_XXXX.json</c>.</summary>
public sealed class RefineThreadBatchFile
{
    public int BatchIndex { get; set; }
    public int TotalBatches { get; set; }
    public string BeforeText { get; set; } = "";
    /// <summary>Null while the OpenAI call for this batch is in flight; set when the response is written.</summary>
    public string? AfterText { get; set; }

    public static void Write(string directory, int batchIndex0, int totalBatches, string beforeText, string? afterText)
    {
        Directory.CreateDirectory(directory);
        var name = $"batch_{batchIndex0 + 1:D4}_of_{totalBatches:D4}.json";
        var path = Path.Combine(directory, name);
        var dto = new RefineThreadBatchFile
        {
            BatchIndex = batchIndex0,
            TotalBatches = totalBatches,
            BeforeText = beforeText,
            AfterText = afterText
        };
        File.WriteAllText(path, JsonSerializer.Serialize(dto, new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
    }

    public static string RelativePath(int batchIndex0, int totalBatches) =>
        $"refiner_threads/batch_{batchIndex0 + 1:D4}_of_{totalBatches:D4}.json".Replace('\\', '/');
}
