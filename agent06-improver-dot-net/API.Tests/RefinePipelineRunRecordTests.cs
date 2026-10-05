using System.Text.Json;
using TranslationImprover.Features.Refine.Application;
using TranslationImprover.Features.Refine.Domain;
using TranslationImprover.Features.Refine.Infrastructure;
using Xunit;

namespace API.Tests;

/// <summary>
/// A refine run must leave a trace of HOW it was produced: the prompts it used next to the output
/// (&lt;output&gt;.prompt.md) and every batch with its line range and the context (seam) that was sent.
/// </summary>
public class RefinePipelineRunRecordTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "refine-run-record-" + Guid.NewGuid().ToString("N"));

    public RefinePipelineRunRecordTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    /// <summary>Echoes the batch upper-cased and remembers the system prompt it was given.</summary>
    private sealed class UpperCaseClient : IOpenAIRefineClient
    {
        public List<string> SystemPrompts { get; } = new();

        public Task<BatchResult> RefineBatchAsync(BatchInfo batchInfo, string model, float temperature, string systemPrompt,
            string userPromptTemplate, string? baseUrlOverride = null, CancellationToken cancellationToken = default)
        {
            SystemPrompts.Add(systemPrompt);
            return Task.FromResult(new BatchResult
            {
                BatchIndex = batchInfo.Index,
                Success = true,
                FixedLines = batchInfo.Lines.Select(l => l.TrimEnd('\n').ToUpperInvariant() + "\n").ToList()
            });
        }
    }

    [Fact]
    public async Task Run_WritesPromptRecordAndBatchesWithSeams_UsingCustomSystemPrompt()
    {
        File.WriteAllText(Path.Combine(_root, "my_prompt.txt"), "MY TEMPLATE {context} {batch}");
        var store = new InMemoryRefineJobStore();
        var client = new UpperCaseClient();
        var pipeline = new RefinePipeline(store, client, new FilePromptLoader());
        var jobId = store.Create();

        await pipeline.RunAsync(jobId, new RefineJobRequest
        {
            InputContent = "a1\na2\na3\na4\na5",
            OutputFilePath = "transcript_fixed_20261005_112233_444.md",
            BatchSize = 2,
            ContextLines = 1,
            Model = "gpt-4o-mini",
            PromptFile = "my_prompt.txt",
            SystemPrompt = "MY SYSTEM PROMPT",
            SourceName = "transcript_20261005_103729_357.md",
            SaveIntermediate = true,
            IntermediateDir = "refine_batches"
        }, _root, _root);

        Assert.All(client.SystemPrompts, p => Assert.Equal("MY SYSTEM PROMPT", p));
        Assert.Equal("A1\nA2\nA3\nA4\nA5", File.ReadAllText(Path.Combine(_root, "transcript_fixed_20261005_112233_444.md")).Trim().Replace("\r\n", "\n"));

        var record = File.ReadAllText(Path.Combine(_root, "transcript_fixed_20261005_112233_444.prompt.md"));
        Assert.Contains("- Source: transcript_20261005_103729_357.md", record);
        Assert.Contains("- Lines per block: 2, context lines: 1", record);
        Assert.Contains("MY SYSTEM PROMPT", record);
        Assert.Contains("MY TEMPLATE {context} {batch}", record);
        Assert.DoesNotContain("## System prompt (built-in)", record);

        var batchFiles = Directory.GetFiles(Path.Combine(_root, "refine_batches"), "batch_*.json").Order().ToList();
        Assert.Equal(3, batchFiles.Count);
        using var second = JsonDocument.Parse(File.ReadAllText(batchFiles[1]));
        Assert.Equal(3, second.RootElement.GetProperty("startLine").GetInt32());
        Assert.Equal(4, second.RootElement.GetProperty("endLine").GetInt32());
        // The seam: block 2 was sent with the last REFINED line of block 1 as context.
        Assert.Equal("A2", second.RootElement.GetProperty("contextText").GetString());
        Assert.Equal("a3\na4", second.RootElement.GetProperty("originalText").GetString());
        Assert.Equal("A3\nA4", second.RootElement.GetProperty("refinedText").GetString());
    }
}
