using System.Net;
using System.Text;
using Agent04.Features.Transcription.Application;
using Agent04.Features.Transcription.Infrastructure;
using Xunit;

namespace Agent04.Tests;

/// <summary>The model of a call: the job's own setting when it names one, the model of the service otherwise.</summary>
public sealed class OpenAITranscriptionClientModelTests : IDisposable
{
    private readonly string _audioPath = Path.Combine(Path.GetTempPath(), "agent04_client_model_" + Guid.NewGuid().ToString("N") + ".wav");

    public OpenAITranscriptionClientModelTests() => File.WriteAllBytes(_audioPath, new byte[] { 1, 2, 3 });

    public void Dispose()
    {
        try { File.Delete(_audioPath); } catch { /* ignore */ }
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly string _answer;
        public List<Dictionary<string, string>> Requests { get; } = new();

        public RecordingHandler(string answer) => _answer = answer;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var fields = new Dictionary<string, string>();
            var form = await request.Content!.ReadAsMultipartAsync(cancellationToken);
            foreach (var part in form)
            {
                var name = part.Headers.ContentDisposition?.Name?.Trim('"') ?? "";
                if (name != "file")
                    fields[name] = await part.ReadAsStringAsync(cancellationToken);
            }

            Requests.Add(fields);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_answer, Encoding.UTF8, "application/json"),
            };
        }
    }

    private static (OpenAITranscriptionClient Client, RecordingHandler Handler) CreateClient(string answer)
    {
        var handler = new RecordingHandler(answer);
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.example.invalid/") };
        return (new OpenAITranscriptionClient(http, "test-key", "gpt-4o-transcribe-diarize"), handler);
    }

    [Fact]
    public async Task Call_without_a_model_uses_the_model_of_the_service()
    {
        var (client, handler) = CreateClient("""{ "text": "hola", "segments": [ { "start": 0.5, "end": 1.2, "text": "hola", "speaker": "A" } ] }""");

        await client.TranscribeAsync(_audioPath, new TranscriptionClientOptions
        {
            ResponseFormat = "diarized_json",
            ChunkingStrategy = "auto",
        });

        var request = Assert.Single(handler.Requests);
        Assert.Equal("gpt-4o-transcribe-diarize", request["model"]);
        Assert.Equal("diarized_json", request["response_format"]);
        Assert.Equal("auto", request["chunking_strategy"]);
    }

    [Fact]
    public async Task Call_with_a_model_of_the_job_sends_that_model_and_reads_its_timed_segments()
    {
        // whisper answers verbose_json: segments with times and no speakers
        var (client, handler) = CreateClient(
            """{ "text": "hola qué tal", "segments": [ { "id": 0, "start": 0.5, "end": 1.2, "text": " hola" }, { "id": 1, "start": 3.0, "end": 4.1, "text": " qué tal" } ] }""");

        var result = await client.TranscribeAsync(_audioPath, new TranscriptionClientOptions
        {
            Model = "whisper-1",
            Language = "es",
            ResponseFormat = "verbose_json",
        });

        var request = Assert.Single(handler.Requests);
        Assert.Equal("whisper-1", request["model"]);
        Assert.Equal("verbose_json", request["response_format"]);
        Assert.False(request.ContainsKey("chunking_strategy"));
        Assert.Equal(2, result.Segments.Count);
        Assert.Equal(3.0, result.Segments[1].Start, 6);
        Assert.Equal("qué tal", result.Segments[1].Text);
        Assert.Null(result.Segments[1].Speaker);
    }
}

internal static class MultipartReading
{
    /// <summary>Reads the parts of a multipart/form-data body the client built (the content is in memory).</summary>
    public static async Task<List<HttpContent>> ReadAsMultipartAsync(this HttpContent content, CancellationToken ct)
    {
        if (content is MultipartFormDataContent form)
            return form.ToList();

        await content.LoadIntoBufferAsync();
        _ = ct;
        return new List<HttpContent>();
    }
}
