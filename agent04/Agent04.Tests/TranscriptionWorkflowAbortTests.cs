using System.Net.Http;
using Agent04.Features.Transcription.Application;
using Xunit;

namespace Agent04.Tests;

public sealed class TranscriptionWorkflowAbortTests
{
    [Fact]
    public void ShouldAbortWholeJob_true_for_401_message()
    {
        var ex = new HttpRequestException("401: {\"error\":{\"message\":\"invalid\"}}");
        Assert.True(TranscriptionWorkflowAbort.ShouldAbortWholeJob(ex));
    }

    [Fact]
    public void ShouldAbortWholeJob_true_for_403_message()
    {
        var ex = new HttpRequestException("403: forbidden");
        Assert.True(TranscriptionWorkflowAbort.ShouldAbortWholeJob(ex));
    }

    [Fact]
    public void ShouldAbortWholeJob_true_for_insufficient_quota_in_body()
    {
        var ex = new InvalidOperationException("{\"error\":{\"type\":\"insufficient_quota\"}}");
        Assert.True(TranscriptionWorkflowAbort.ShouldAbortWholeJob(ex));
    }

    [Fact]
    public void ShouldAbortWholeJob_false_for_500()
    {
        var ex = new HttpRequestException("500: server error");
        Assert.False(TranscriptionWorkflowAbort.ShouldAbortWholeJob(ex));
    }
}
