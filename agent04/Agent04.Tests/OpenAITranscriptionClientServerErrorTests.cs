using System.Net;
using System.Text;
using Agent04.Features.Transcription.Application;
using Agent04.Features.Transcription.Infrastructure;
using Xunit;

namespace Agent04.Tests;

/// <summary>
/// Three server errors (HTTP 5xx) of the request with time marks: the client tells the caller who asked
/// for it, and falls back to text without time marks for everyone else, as it always did.
/// </summary>
public sealed class OpenAITranscriptionClientServerErrorTests : IDisposable
{
    private const string ServerError = """{ "error": { "message": "The server had an error processing your request.", "type": "server_error" } }""";
    private const string BadRequest = """{ "error": { "message": "Unsupported parameter.", "type": "invalid_request_error" } }""";
    private const string Timed = """{ "text": "hola", "segments": [ { "start": 0.5, "end": 1.2, "text": "hola", "speaker": "A" } ] }""";
    private const string Plain = """{ "text": "hola sin marcas" }""";

    private readonly string _audioPath = Path.Combine(Path.GetTempPath(), "agent04_client_5xx_" + Guid.NewGuid().ToString("N") + ".wav");

    public OpenAITranscriptionClientServerErrorTests() => File.WriteAllBytes(_audioPath, new byte[] { 1, 2, 3 });

    public void Dispose()
    {
        try { File.Delete(_audioPath); } catch { /* ignore */ }
    }

    /// <summary>Answers by a script that sees the form fields of the request and its number (from 1).</summary>
    private sealed class ScriptedHandler : HttpMessageHandler
    {
        private readonly Func<Dictionary<string, string>, int, (HttpStatusCode Status, string Body)> _script;
        public List<Dictionary<string, string>> Requests { get; } = new();

        public ScriptedHandler(Func<Dictionary<string, string>, int, (HttpStatusCode, string)> script) => _script = script;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var fields = new Dictionary<string, string>();
            foreach (var part in (MultipartFormDataContent)request.Content!)
            {
                var name = part.Headers.ContentDisposition?.Name?.Trim('"') ?? "";
                if (name != "file")
                    fields[name] = await part.ReadAsStringAsync(cancellationToken);
            }

            Requests.Add(fields);
            var (status, body) = _script(fields, Requests.Count);
            return new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }

    private static (OpenAITranscriptionClient Client, ScriptedHandler Handler) CreateClient(
        Func<Dictionary<string, string>, int, (HttpStatusCode, string)> script)
    {
        var handler = new ScriptedHandler(script);
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.example.invalid/") };
        var client = new OpenAITranscriptionClient(http, "test-key", "gpt-4o-transcribe-diarize") { RetryDelayUnit = TimeSpan.Zero };
        return (client, handler);
    }

    private static TranscriptionClientOptions Options(bool stopAtServerError) => new()
    {
        Language = "es",
        ResponseFormat = "diarized_json",
        ChunkingStrategy = "auto",
        StopAtServerError = stopAtServerError,
    };

    private static bool IsTimed(Dictionary<string, string> fields) => fields.ContainsKey("response_format");

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task Three_server_errors_of_the_timed_request_are_reported_to_a_caller_who_asked(HttpStatusCode status)
    {
        // the server takes the same audio without response_format: the caller must not get that answer
        var (client, handler) = CreateClient((fields, _) => IsTimed(fields) ? (status, ServerError) : (HttpStatusCode.OK, Plain));

        var error = await Assert.ThrowsAsync<TranscriptionServerErrorException>(
            () => client.TranscribeAsync(_audioPath, Options(stopAtServerError: true)));

        Assert.Equal((int)status, error.StatusCode);
        Assert.Equal(3, error.Attempts);
        Assert.Equal(3, handler.Requests.Count);
        Assert.All(handler.Requests, r => Assert.Equal("diarized_json", r["response_format"]));
    }

    [Fact]
    public async Task Without_that_word_the_client_goes_on_to_text_without_time_marks()
    {
        var (client, handler) = CreateClient((fields, _) =>
            IsTimed(fields) ? (HttpStatusCode.InternalServerError, ServerError) : (HttpStatusCode.OK, Plain));

        var result = await client.TranscribeAsync(_audioPath, Options(stopAtServerError: false));

        Assert.Equal(4, handler.Requests.Count);
        Assert.False(IsTimed(handler.Requests[3]));
        var only = Assert.Single(result.Segments);
        Assert.Equal("hola sin marcas", only.Text);
        Assert.Equal(0.0, only.Start);
        Assert.Equal(0.0, only.End);
    }

    [Fact]
    public async Task A_server_error_that_passes_on_a_retry_is_not_reported()
    {
        var (client, handler) = CreateClient((_, number) =>
            number == 1 ? (HttpStatusCode.InternalServerError, ServerError) : (HttpStatusCode.OK, Timed));

        var result = await client.TranscribeAsync(_audioPath, Options(stopAtServerError: true));

        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal(0.5, Assert.Single(result.Segments).Start, 6);
    }

    [Fact]
    public async Task A_client_error_is_not_a_server_error()
    {
        var (client, handler) = CreateClient((fields, _) =>
            IsTimed(fields) ? (HttpStatusCode.BadRequest, BadRequest) : (HttpStatusCode.OK, Plain));

        var result = await client.TranscribeAsync(_audioPath, Options(stopAtServerError: true));

        // not retried, the next parameter set is tried at once
        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal("hola sin marcas", Assert.Single(result.Segments).Text);
    }

    [Fact]
    public async Task Server_errors_of_a_later_parameter_set_are_not_reported()
    {
        // 1: timed request, 400. 2–4: the same without response_format, 500 three times.
        // 5: timed with chunking "none", answered.
        var (client, handler) = CreateClient((_, number) => number switch
        {
            1 => (HttpStatusCode.BadRequest, BadRequest),
            <= 4 => (HttpStatusCode.InternalServerError, ServerError),
            _ => (HttpStatusCode.OK, Timed),
        });

        var result = await client.TranscribeAsync(_audioPath, Options(stopAtServerError: true));

        Assert.Equal(5, handler.Requests.Count);
        Assert.Equal("none", handler.Requests[4]["chunking_strategy"]);
        Assert.Equal(0.5, Assert.Single(result.Segments).Start, 6);
    }
}
