using System.Text.Json;
using System.Text.Json.Serialization;
using TranslationImprover.Features.Refine.Application;

namespace TranslationImprover.Features.Refine.Infrastructure;

/// <summary>Persisted under job's <c>refiner_threads/checkpoint.json</c> for pause/resume.</summary>
public sealed class RefineCheckpoint
{
    public int SchemaVersion { get; set; } = 1;
    public string JobId { get; set; } = "";
    /// <summary>0-based index of the next batch to run.</summary>
    public int NextBatchIndex { get; set; }
    public int TotalBatches { get; set; }
    public List<string> FixedLines { get; set; } = new();
    public List<string>? PreviousContextLines { get; set; }
    public List<string> HeaderLines { get; set; } = new();
    public List<string> FooterLines { get; set; } = new();
    /// <summary>Full content lines (with trailing newlines per line) for re-batching.</summary>
    public List<string> ContentLines { get; set; } = new();
    public RefineJobRequestDto Request { get; set; } = new();

    public static RefineCheckpoint? TryLoad(string path)
    {
        if (!File.Exists(path)) return null;
        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<RefineCheckpoint>(json, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    public void Save(string path)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        File.WriteAllText(path, JsonSerializer.Serialize(this, JsonOptions));
    }

    public RefineJobRequest ToRequest() => Request.ToModel();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}

/// <summary>JSON-serializable copy of <see cref="RefineJobRequest"/>.</summary>
public sealed class RefineJobRequestDto
{
    public string? InputFilePath { get; set; }
    public string? InputContent { get; set; }
    public string? OutputFilePath { get; set; }
    public int BatchSize { get; set; } = 10;
    public int ContextLines { get; set; } = 3;
    public string Model { get; set; } = "gpt-4o-mini";
    public float Temperature { get; set; }
    public string? PromptFile { get; set; }
    public string? OpenAIBaseUrl { get; set; }
    public string? OpenAIOrganization { get; set; }
    public bool SaveIntermediate { get; set; } = true;
    public string? IntermediateDir { get; set; }
    public string? CallbackUrl { get; set; }
    public List<string>? Tags { get; set; }
    public string? JobDirectoryRelative { get; set; }
    public string? WorkspaceRootOverride { get; set; }

    public static RefineJobRequestDto FromModel(RefineJobRequest r) =>
        new()
        {
            InputFilePath = r.InputFilePath,
            InputContent = r.InputContent,
            OutputFilePath = r.OutputFilePath,
            BatchSize = r.BatchSize,
            ContextLines = r.ContextLines,
            Model = r.Model,
            Temperature = r.Temperature,
            PromptFile = r.PromptFile,
            OpenAIBaseUrl = r.OpenAIBaseUrl,
            OpenAIOrganization = r.OpenAIOrganization,
            SaveIntermediate = r.SaveIntermediate,
            IntermediateDir = r.IntermediateDir,
            CallbackUrl = r.CallbackUrl,
            Tags = r.Tags?.ToList(),
            JobDirectoryRelative = r.JobDirectoryRelative,
            WorkspaceRootOverride = r.WorkspaceRootOverride
        };

    public RefineJobRequest ToModel() =>
        new()
        {
            InputFilePath = InputFilePath,
            InputContent = InputContent,
            OutputFilePath = OutputFilePath,
            BatchSize = BatchSize,
            ContextLines = ContextLines,
            Model = Model,
            Temperature = Temperature,
            PromptFile = PromptFile,
            OpenAIBaseUrl = OpenAIBaseUrl,
            OpenAIOrganization = OpenAIOrganization,
            SaveIntermediate = SaveIntermediate,
            IntermediateDir = IntermediateDir,
            CallbackUrl = CallbackUrl,
            Tags = Tags,
            JobDirectoryRelative = JobDirectoryRelative,
            WorkspaceRootOverride = WorkspaceRootOverride
        };
}
