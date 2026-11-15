using Microsoft.AspNetCore.Mvc;
using TranslationImprover.Application;
using TranslationImprover.Controllers;
using TranslationImprover.Features.Refine.Application;
using TranslationImprover.Features.Refine.Infrastructure;
using TranslationImprover.Features.RefineJobQuery.Infrastructure;
using Xunit;

namespace API.Tests;

/// <summary>
/// What a REST request asks for reaches the pipeline. The controller copies the request field by field, and
/// the check of edits, the system prompt and the source name were once left out of that copy: a REST run asked
/// with <c>respellingOnly</c> took the model's lines as they were, with no word about it.
/// </summary>
public class RefineControllerSubmitTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "refine-rest-submit-" + Guid.NewGuid().ToString("N"));

    public RefineControllerSubmitTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    private sealed class RecordingPipeline : IRefinePipeline
    {
        public RefineJobRequest? Seen { get; private set; }

        public Task RunAsync(string jobId, RefineJobRequest request, string workspaceRoot, string artifactRoot, CancellationToken cancellationToken = default)
        {
            Seen = request;
            return Task.CompletedTask;
        }
    }

    private (RefineController Controller, RecordingPipeline Pipeline) Build()
    {
        var store = new InMemoryRefineJobStore();
        var pipeline = new RecordingPipeline();
        var cancellation = new InMemoryRefineJobCancellation();
        var workspace = new WorkspaceRoot(_root);
        var controller = new RefineController(store, new RefineJobSubmitter(store, pipeline, cancellation, workspace), cancellation, new RefineJobQueryService(store), workspace);
        return (controller, pipeline);
    }

    [Fact]
    public void Submit_PassesTheCheckOfEditsTheSystemPromptAndTheSourceName()
    {
        var (controller, pipeline) = Build();

        var result = controller.Submit(
            new RefineJobRequest
            {
                InputContent = "- 1.00 Olga: \"vale\"\n",
                RespellingOnly = true,
                SystemPrompt = "  Copy the lines.  ",
                SourceName = " lesson.md "
            },
            xCallerId: null,
            CancellationToken.None);

        Assert.IsType<AcceptedAtActionResult>(result);
        Assert.NotNull(pipeline.Seen);
        Assert.True(pipeline.Seen!.RespellingOnly);
        Assert.Equal("Copy the lines.", pipeline.Seen.SystemPrompt);
        Assert.Equal("lesson.md", pipeline.Seen.SourceName);
    }

    [Fact]
    public void Submit_WithoutThem_LeavesTheCheckOffAndThePromptBuiltIn()
    {
        var (controller, pipeline) = Build();

        controller.Submit(new RefineJobRequest { InputContent = "- 1.00 Olga: \"vale\"\n", SystemPrompt = "   " }, xCallerId: null, CancellationToken.None);

        Assert.NotNull(pipeline.Seen);
        Assert.False(pipeline.Seen!.RespellingOnly);
        Assert.Null(pipeline.Seen.SystemPrompt);
        Assert.Null(pipeline.Seen.SourceName);
    }
}
