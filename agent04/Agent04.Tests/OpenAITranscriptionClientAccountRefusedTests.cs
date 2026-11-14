using System.Net;
using System.Text;
using Agent04.Features.Transcription.Application;
using Agent04.Features.Transcription.Infrastructure;
using Xunit;

namespace Agent04.Tests;

/// <summary>
/// A refusal that concerns the account (no credits, the key): the client says so after one request and
/// tries neither another parameter set nor another model — each would be refused the same way.
/// </summary>
public sealed class OpenAITranscriptionClientAccountRefusedTests : IDisposable
{
    private const string NoCredits = """{ "error": { "message": "You have no credits remaining. Add credits to continue.", "type": "insufficient_quota", "param": null, "code": "insufficient_quota" } }""";
    private const string BadKey = """{ "error": { "message": "Incorrect API key provided.", "type": "invalid_request_error", "code": "invalid_api_key" } }""";
    private const string TooFast = """{ "error": { "message": "Rate limit reached for requests.", "type": "requests", "code": "rate_limit_exceeded" } }""";
    private const string Plain = """{ "text": "hola" }""";

    private readonly string _audioPath = Path.Combine(Path.GetTempPath(), "agent04_client_refused_" + Guid.NewGuid().ToString("N") + ".wav");

    public OpenAITranscriptionClientAccountRefusedTests() => File.WriteAllBytes(_audioPath, new byte[] { 1, 2, 3 });

    public void Dispose()
    {
        try { File.Delete(_audioPath); } catch { /* ignore */ }
    }

    /// <summary>Answers by the number of the request (from 1) and counts the requests.</summary>
    private sealed class CountingHandler : HttpMessageHandler
    {
        private readonly Func<int, (HttpStatusCode Status, string Body)> _script;
        private int _requests;

        public CountingHandler(Func<int, (HttpStatusCode, string)> script) => _script = script;

        public int Requests => _requests;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var (status, body) = _script(Interlocked.Increment(ref _requests));
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }

    private static (OpenAITranscriptionClient Client, CountingHandler Handler) CreateClient(
        Func<int, (HttpStatusCode, string)> script, IReadOnlyList<string>? fallbackModels = null)
    {
        var handler = new CountingHandler(script);
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.example.invalid/") };
        var client = new OpenAITranscriptionClient(http, "test-key", "gpt-4o-transcribe-diarize", fallbackModels) { RetryDelayUnit = TimeSpan.Zero };
        return (client, handler);
    }

    /// <summary>The request of a diarize job: several parameter sets and other models stand behind it.</summary>
    private static TranscriptionClientOptions Options() => new()
    {
        Language = "es",
        ResponseFormat = "diarized_json",
        ChunkingStrategy = "auto",
    };

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests, NoCredits, "insufficient_quota", "no credits left")]
    [InlineData(HttpStatusCode.Unauthorized, BadKey, "invalid_api_key", "API key")]
    public async Task A_refusal_of_the_account_is_told_after_one_request(HttpStatusCode status, string body, string reason, string says)
    {
        var (client, handler) = CreateClient(_ => (status, body), fallbackModels: new[] { "whisper-1" });

        var refused = await Assert.ThrowsAsync<TranscriptionAccountRefusedException>(
            () => client.TranscribeAsync(_audioPath, Options()));

        Assert.Equal(reason, refused.Reason);
        Assert.Contains(says, refused.Message);
        Assert.Equal(1, handler.Requests);
        Assert.True(TranscriptionWorkflowAbort.ShouldAbortWholeJob(refused));
    }

    [Fact]
    public async Task Too_many_requests_at_once_is_not_a_refusal_of_the_account()
    {
        // the same HTTP 429, another reason: the next parameter set is tried, as before
        var (client, handler) = CreateClient(number => number == 1 ? (HttpStatusCode.TooManyRequests, TooFast) : (HttpStatusCode.OK, Plain));

        var result = await client.TranscribeAsync(_audioPath, Options());

        Assert.Equal(2, handler.Requests);
        Assert.Equal("hola", Assert.Single(result.Segments).Text);
    }
}
