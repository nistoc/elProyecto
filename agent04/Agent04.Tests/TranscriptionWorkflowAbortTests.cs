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

    [Theory]
    [InlineData("429: {\"error\":{\"type\":\"insufficient_quota\",\"code\":\"insufficient_quota\"}}", "insufficient_quota")]
    [InlineData("401: {\"error\":{\"code\":\"invalid_api_key\"}}", "invalid_api_key")]
    [InlineData("401: {\"error\":{\"message\":\"no\"}}", "401")]
    [InlineData("403: forbidden", "403")]
    [InlineData("429: {\"error\":{\"type\":\"rate_limit_exceeded\"}}", null)]
    [InlineData("500: server error", null)]
    public void The_reason_of_a_refusal_is_the_servers_word_for_it(string message, string? expected)
    {
        // as the client wraps it: the HTTP failure inside "failed for all models"
        var ex = new InvalidOperationException("Transcription failed for all models", new HttpRequestException(message));

        Assert.Equal(expected, TranscriptionWorkflowAbort.RefusalReason(ex));
    }

    [Fact]
    public void An_account_refusal_stops_the_whole_job_and_says_what_to_do()
    {
        var refused = new TranscriptionAccountRefusedException("insufficient_quota", "You have no credits remaining.");

        Assert.True(TranscriptionWorkflowAbort.ShouldAbortWholeJob(new InvalidOperationException("chunk failed", refused)));
        Assert.Equal("insufficient_quota", TranscriptionWorkflowAbort.RefusalReason(refused));
        Assert.Contains("no credits left", refused.Message);
        Assert.Contains("insufficient_quota", refused.Message);
        Assert.Contains("API key", new TranscriptionAccountRefusedException("invalid_api_key", "").Message);
    }
}
