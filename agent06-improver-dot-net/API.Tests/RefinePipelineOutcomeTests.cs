using System.Text;
using System.Text.Json;
using TranslationImprover.Features.Refine.Application;
using TranslationImprover.Features.Refine.Domain;
using TranslationImprover.Features.Refine.Infrastructure;
using Xunit;

namespace API.Tests;

/// <summary>
/// What a run leaves behind besides its lines: the blocks that failed and the tokens billed are in the job's
/// status and in the run record (a failed block used to be seen in the log only, the job read Completed);
/// every block's record holds the model's answer as it came; the output has the form of the source; a run
/// that cannot read its transcript ends Failed instead of staying Pending for ever.
/// </summary>
public class RefinePipelineOutcomeTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "refine-outcome-" + Guid.NewGuid().ToString("N"));

    public RefinePipelineOutcomeTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    private const string Transcript =
        ">>>>>>>\n"
        + "- 1.00 Olga: \"Vale, tengo una casa.\"\n"
        + "- 2.00 Maria: \"Sí, вале, это мой дом.\"\n"
        + "- 3.00 Olga: \"Сейчас приду.\"\n"
        + "- 4.00 Maria: \"Tengo una casa.\"\n"
        + "- 5.00 Olga: \"Хорошо.\"\n"
        + "<<<<<\n";

    /// <summary>Respells one word, translates another, fails on the block it is told to, and reports its tokens.</summary>
    private sealed class ScriptedClient : IOpenAIRefineClient
    {
        public int? FailOnBlock { get; init; }

        public Task<BatchResult> RefineBatchAsync(BatchInfo batchInfo, string model, float temperature, string systemPrompt,
            string userPromptTemplate, string? baseUrlOverride = null, CancellationToken cancellationToken = default)
        {
            if (batchInfo.Index == FailOnBlock)
                return Task.FromResult(new BatchResult { BatchIndex = batchInfo.Index, Success = false, Error = "HTTP 500", FixedLines = batchInfo.Lines.ToList() });

            var lines = batchInfo.Lines.Select(l => l.Replace("вале", "vale").Replace("мой дом", "mi casa")).ToList();
            return Task.FromResult(new BatchResult
            {
                BatchIndex = batchInfo.Index,
                Success = true,
                FixedLines = lines,
                RawAnswer = string.Concat(lines),
                PromptTokens = 100,
                CompletionTokens = 20
            });
        }
    }

    private async Task<(RefineJobStatus Job, string Record)> RunAsync(IOpenAIRefineClient client, RefineJobRequest request)
    {
        var store = new InMemoryRefineJobStore();
        var pipeline = new RefinePipeline(store, client, new FilePromptLoader());
        var jobId = store.Create();
        await pipeline.RunAsync(jobId, request, _root, _root);
        var recordPath = request.OutputFilePath == null ? null : Path.Combine(_root, Path.GetFileNameWithoutExtension(request.OutputFilePath) + ".prompt.md");
        return (store.Get(jobId)!, recordPath != null && File.Exists(recordPath) ? File.ReadAllText(recordPath).Replace("\r\n", "\n") : "");
    }

    [Fact]
    public async Task A_failed_block_is_counted_in_the_status_and_named_in_the_record()
    {
        var (job, record) = await RunAsync(new ScriptedClient { FailOnBlock = 0 }, new RefineJobRequest
        {
            InputContent = Transcript,
            OutputFilePath = "transcript_fixed_failed.md",
            BatchSize = 2,
            ContextLines = 1,
            SaveIntermediate = true,
            IntermediateDir = "refine_batches"
        });

        Assert.Equal(RefineJobState.Completed, job.State);
        Assert.Equal(1, job.Totals.FailedBatches);
        Assert.Equal(200, job.Totals.PromptTokens);
        Assert.Equal(40, job.Totals.CompletionTokens);
        Assert.False(job.Batches[0].Ok);
        Assert.True(job.Batches[1].Ok);

        // The failed block's lines are in the output as they were.
        var output = File.ReadAllText(Path.Combine(_root, "transcript_fixed_failed.md"));
        Assert.Contains("- 2.00 Maria: \"Sí, вале, это мой дом.\"", output);

        Assert.Contains("## Outcome", record);
        Assert.Contains("- Blocks: 3, FAILED: 1", record);
        Assert.Contains("- Tokens billed: 200 in, 40 out", record);

        using var first = JsonDocument.Parse(File.ReadAllText(Path.Combine(_root, "refine_batches", "batch_0001_of_0003.json")));
        Assert.False(first.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("HTTP 500", first.RootElement.GetProperty("error").GetString());
        Assert.Equal("", first.RootElement.GetProperty("rawText").GetString());
    }

    [Fact]
    public async Task A_run_without_failures_says_so_and_a_client_without_usage_is_not_given_tokens()
    {
        var (job, record) = await RunAsync(new ScriptedClient(), new RefineJobRequest
        {
            InputContent = Transcript,
            OutputFilePath = "transcript_fixed_ok.md",
            BatchSize = 5,
            ContextLines = 0
        });

        Assert.Equal(0, job.Totals.FailedBatches);
        Assert.Contains("- Blocks: 1, none failed", record);
        Assert.Contains("- Tokens billed: 100 in, 20 out", record);

        var (_, silent) = await RunAsync(new SilentClient(), new RefineJobRequest
        {
            InputContent = Transcript,
            OutputFilePath = "transcript_fixed_silent.md",
            BatchSize = 5,
            ContextLines = 0
        });
        Assert.Contains("- Tokens billed: not reported by the API", silent);
    }

    /// <summary>Gives the lines back and says nothing of tokens, as an endpoint without a usage block does.</summary>
    private sealed class SilentClient : IOpenAIRefineClient
    {
        public Task<BatchResult> RefineBatchAsync(BatchInfo batchInfo, string model, float temperature, string systemPrompt,
            string userPromptTemplate, string? baseUrlOverride = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(new BatchResult { BatchIndex = batchInfo.Index, Success = true, FixedLines = batchInfo.Lines.ToList() });
    }

    [Fact]
    public async Task With_the_check_the_block_record_keeps_what_the_model_wrote_next_to_what_was_kept()
    {
        var (job, _) = await RunAsync(new ScriptedClient(), new RefineJobRequest
        {
            InputContent = Transcript,
            OutputFilePath = "transcript_fixed_checked.md",
            BatchSize = 5,
            ContextLines = 0,
            RespellingOnly = true,
            SaveIntermediate = true,
            IntermediateDir = "refine_batches"
        });

        Assert.Equal(1, job.Totals.EditsLetThrough);
        Assert.Equal(2, job.Totals.EditsPutBack);

        using var block = JsonDocument.Parse(File.ReadAllText(Path.Combine(_root, "refine_batches", "batch_0001_of_0001.json")));
        Assert.Contains("Sí, vale, это mi casa.", block.RootElement.GetProperty("rawText").GetString());
        Assert.Contains("Sí, vale, это мой дом.", block.RootElement.GetProperty("refinedText").GetString());
        Assert.Equal(1, block.RootElement.GetProperty("editsLetThrough").GetInt32());
        Assert.Equal(2, block.RootElement.GetProperty("editsPutBack").GetInt32());
        Assert.Equal(100, block.RootElement.GetProperty("promptTokens").GetInt32());
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public async Task The_output_has_the_form_of_the_source_file(bool bom, bool crlf)
    {
        var text = crlf ? Transcript.Replace("\n", "\r\n") : Transcript;
        var bytes = (bom ? new byte[] { 0xEF, 0xBB, 0xBF } : Array.Empty<byte>()).Concat(new UTF8Encoding(false).GetBytes(text)).ToArray();
        File.WriteAllBytes(Path.Combine(_root, "transcript.md"), bytes);

        var (job, _) = await RunAsync(new SilentClient(), new RefineJobRequest
        {
            InputFilePath = "transcript.md",
            OutputFilePath = "transcript_fixed_form.md",
            BatchSize = 2,
            ContextLines = 1
        });

        Assert.Equal(RefineJobState.Completed, job.State);
        Assert.Equal(bytes, File.ReadAllBytes(Path.Combine(_root, "transcript_fixed_form.md")));
    }

    [Fact]
    public async Task A_transcript_that_cannot_be_read_fails_the_job()
    {
        var (job, _) = await RunAsync(new SilentClient(), new RefineJobRequest { InputFilePath = "absent.md", OutputFilePath = "never.md" });

        Assert.Equal(RefineJobState.Failed, job.State);
        Assert.False(string.IsNullOrEmpty(job.ErrorMessage));
        Assert.False(File.Exists(Path.Combine(_root, "never.md")));
    }
}
