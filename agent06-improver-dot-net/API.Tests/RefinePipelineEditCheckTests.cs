using TranslationImprover.Features.Refine.Application;
using TranslationImprover.Features.Refine.Domain;
using TranslationImprover.Features.Refine.Infrastructure;
using Xunit;

namespace API.Tests;

/// <summary>
/// A run asked with <c>RespellingOnly</c> writes the lines as they were, save for Spanish words respelled from
/// Cyrillic; a run without it takes the model's lines as they are. Either way the run record says which it was.
/// </summary>
public class RefinePipelineEditCheckTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "refine-edit-check-" + Guid.NewGuid().ToString("N"));

    public RefinePipelineEditCheckTests() => Directory.CreateDirectory(_root);

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
        + "- 5.00 Olga: \"Ну, вале, пошли.\"\n"
        + "<<<<<\n";

    /// <summary>A model that respells one word as asked and does three things nobody asked for.</summary>
    private sealed class OverEagerClient : IOpenAIRefineClient
    {
        public List<string> ContextsSeen { get; } = new();

        public Task<BatchResult> RefineBatchAsync(BatchInfo batchInfo, string model, float temperature, string systemPrompt,
            string userPromptTemplate, string? baseUrlOverride = null, CancellationToken cancellationToken = default)
        {
            ContextsSeen.Add(string.Join("", batchInfo.Context ?? Array.Empty<string>()));
            var lines = batchInfo.Lines.Select(l => l
                .Replace("вале", "vale")            // the respelling asked for
                .Replace("мой дом", "mi casa")      // a translation
                .Replace("Сейчас", "Seichas")       // a Russian word in Latin letters
                .Replace("4.00 Maria: \"Tengo una casa", "4.00 Maria: \"Tengo un casa")) // the speaker's Spanish "corrected"
                .ToList();
            return Task.FromResult(new BatchResult { BatchIndex = batchInfo.Index, Success = true, FixedLines = lines });
        }
    }

    private async Task<(string Output, string Record, OverEagerClient Client)> RunAsync(bool respellingOnly)
    {
        var store = new InMemoryRefineJobStore();
        var client = new OverEagerClient();
        var pipeline = new RefinePipeline(store, client, new FilePromptLoader());
        var jobId = store.Create();
        var output = respellingOnly ? "transcript_fixed_checked.md" : "transcript_fixed_plain.md";

        await pipeline.RunAsync(jobId, new RefineJobRequest
        {
            InputContent = Transcript,
            OutputFilePath = output,
            BatchSize = 2,
            ContextLines = 1,
            RespellingOnly = respellingOnly,
        }, _root, _root);

        Assert.Equal(RefineJobState.Completed, store.Get(jobId)!.State);
        return (
            File.ReadAllText(Path.Combine(_root, output)).Replace("\r\n", "\n"),
            File.ReadAllText(Path.Combine(_root, Path.GetFileNameWithoutExtension(output) + ".prompt.md")).Replace("\r\n", "\n"),
            client);
    }

    [Fact]
    public async Task With_the_check_only_the_respelled_word_is_kept()
    {
        var (output, record, client) = await RunAsync(respellingOnly: true);

        Assert.Contains("- 2.00 Maria: \"Sí, vale, это мой дом.\"", output);
        Assert.Contains("- 3.00 Olga: \"Сейчас приду.\"", output);
        Assert.Contains("- 4.00 Maria: \"Tengo una casa.\"", output);
        // The same word among Russian words stays: there it cannot be told from a Russian word that sounds like it.
        Assert.Contains("- 5.00 Olga: \"Ну, вале, пошли.\"", output);
        Assert.DoesNotContain("mi casa", output);
        Assert.DoesNotContain("Seichas", output);
        Assert.DoesNotContain("un casa", output);

        Assert.Contains("- Check of edits: on", record);
        Assert.Contains("## Check of edits", record);
        Assert.Contains("- A respelled word must have Spanish next to it", record);
        Assert.Contains("- Changes of the model let through: 1", record);
        Assert.Contains("  - 2.00 «вале,» → «vale,»", record);
        // Put back: "мой" and "дом" of the translation (word for word), the Russian word in Latin letters,
        // the "corrected" article, and the word with no Spanish next to it.
        Assert.Contains("- Changes of the model put back as they were: 5", record);

        // The next block gets the checked lines as its context, not what the model wrote.
        Assert.Contains("Sí, vale, это мой дом.", client.ContextsSeen[1]);
    }

    [Fact]
    public async Task Without_the_check_the_lines_are_the_models()
    {
        var (output, record, _) = await RunAsync(respellingOnly: false);

        Assert.Contains("- 2.00 Maria: \"Sí, vale, это mi casa.\"", output);
        Assert.Contains("- 5.00 Olga: \"Ну, vale, пошли.\"", output);
        Assert.Contains("- 3.00 Olga: \"Seichas приду.\"", output);
        Assert.Contains("- 4.00 Maria: \"Tengo un casa.\"", output);

        Assert.Contains("- Check of edits: off", record);
        Assert.DoesNotContain("## Check of edits", record);
    }
}
