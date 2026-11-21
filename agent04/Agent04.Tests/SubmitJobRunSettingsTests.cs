using System.Text.Json;
using Agent04.Application;
using Agent04.Features.Transcription.Application;
using Agent04.Features.Transcription.Domain;
using Agent04.Features.Transcription.Infrastructure;
using Agent04.Proto;
using Agent04.Services;
using Grpc.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Agent04.Tests;

/// <summary>
/// SubmitJob leaves <c>run_settings.json</c> in the job folder: the same settings it hands to the pipeline.
/// The pipeline here is a stand-in, nothing is transcribed.
/// </summary>
public sealed class SubmitJobRunSettingsTests : IDisposable
{
    private readonly string _workspace = Path.Combine(Path.GetTempPath(), "agent04_submit_" + Guid.NewGuid().ToString("N"));
    private readonly string _jobDir;
    private readonly PipelineSpy _pipeline = new();

    public SubmitJobRunSettingsTests()
    {
        _jobDir = Path.Combine(_workspace, "job01");
        Directory.CreateDirectory(_jobDir);
        Directory.CreateDirectory(Path.Combine(_workspace, "config"));
        File.WriteAllText(
            Path.Combine(_workspace, "config", "default.json"),
            """{ "model": "gpt-4o-transcribe-diarize", "response_format": "diarized_json", "silence_compress_enabled": true, "silence_keep_sec": 1.0 }""");
        File.WriteAllText(Path.Combine(_jobDir, "lesson.m4a"), "audio");
    }

    public void Dispose()
    {
        try { Directory.Delete(_workspace, true); } catch { /* ignore */ }
    }

    private TranscriptionGrpcService Service() => new(
        _pipeline,
        new InMemoryJobStatusStore(),
        new WorkspaceRoot(_workspace),
        new TestHostEnvironment(),
        NullLogger<TranscriptionGrpcService>.Instance,
        new ConfigurationBuilder().Build(),
        new JobArtifactRootRegistry(),
        projectArtifactService: null!,
        merger: null!);

    private static SubmitJobRequest Request() => new()
    {
        ConfigPath = "config/default.json",
        InputFilePath = "job01/lesson.m4a",
    };

    private JsonElement Record()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(RunSettingsFile.ResolvePath(_jobDir)));
        return doc.RootElement.Clone();
    }

    [Fact]
    public async Task Accepted_job_leaves_the_record_of_the_shared_settings()
    {
        var response = await Service().SubmitJob(Request(), new TestCallContext());

        Assert.False(string.IsNullOrEmpty(response.JobId));
        var record = Record();
        Assert.Equal("gpt-4o-transcribe-diarize", record.GetProperty("model").GetString());
        Assert.True(record.GetProperty("silence_compress_enabled").GetBoolean());
        Assert.Equal(1.0, record.GetProperty("silence_keep_sec").GetDouble());
        Assert.Equal(0, record.GetProperty("from_job_settings").GetArrayLength());
        // the pipeline was started with the same settings
        var config = Assert.Single(_pipeline.Configs);
        Assert.Equal(1.0, config.Get<double?>("silence_keep_sec"));
    }

    [Fact]
    public async Task Record_holds_the_settings_of_the_job_and_the_pipeline_gets_the_same()
    {
        File.WriteAllText(
            JobSettingsFile.ResolvePath(_jobDir),
            """{ "schema_version": 1, "transcription": { "silence_keep_sec": 0.2, "model": "whisper-1" } }""");

        await Service().SubmitJob(Request(), new TestCallContext());

        var record = Record();
        Assert.Equal("whisper-1", record.GetProperty("model").GetString());
        Assert.Equal(0.2, record.GetProperty("silence_keep_sec").GetDouble());
        Assert.Equal(
            new[] { "model", "silence_keep_sec" },
            record.GetProperty("from_job_settings").EnumerateArray().Select(x => x.GetString()).ToArray());
        var config = Assert.Single(_pipeline.Configs);
        Assert.Equal("whisper-1", config.Model);
        Assert.Equal(0.2, config.Get<double?>("silence_keep_sec"));
    }

    [Fact]
    public async Task Refused_job_leaves_no_record_and_starts_nothing()
    {
        File.WriteAllText(JobSettingsFile.ResolvePath(_jobDir), """{ "transcription": { "silence_keep_sec": 99 } }""");

        var ex = await Assert.ThrowsAsync<RpcException>(() => Service().SubmitJob(Request(), new TestCallContext()));

        Assert.Equal(StatusCode.InvalidArgument, ex.StatusCode);
        Assert.Contains("silence_keep_sec", ex.Status.Detail);
        Assert.False(File.Exists(RunSettingsFile.ResolvePath(_jobDir)));
        Assert.Empty(_pipeline.Configs);
    }

    private sealed class PipelineSpy : ITranscriptionPipeline
    {
        public List<TranscriptionConfig> Configs { get; } = new();

        public Task<(string MdPath, string JsonPath)> ProcessFileAsync(
            TranscriptionConfig config,
            string inputFilePath,
            string workspaceRoot,
            string? jobId = null,
            IJobStatusStore? statusStore = null,
            INodeModel? nodeModel = null,
            CancellationToken cancellationToken = default,
            IReadOnlySet<int>? chunkIndicesFilter = null)
        {
            Configs.Add(config);
            return Task.FromResult(("", ""));
        }

        public Task TranscribeSplitSubChunkAsync(
            TranscriptionConfig config,
            string artifactRoot,
            string agentJobId,
            int parentChunkIndex,
            int subChunkIndex,
            int totalChunks,
            INodeModel? nodeModel,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task RetranscribeMainChunkAsync(
            TranscriptionConfig config,
            string artifactRoot,
            string agentJobId,
            int chunkIndex,
            INodeModel? nodeModel,
            CancellationToken cancellationToken = default,
            IJobStatusStore? statusStore = null) => throw new NotSupportedException();

        public Task RebuildCombinedOutputsFromPerChunkJsonAsync(
            TranscriptionConfig config,
            string artifactRoot,
            string agentJobId,
            INodeModel? nodeModel,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<LookAgainSummary> LookAgainAtMainChunksAsync(
            TranscriptionConfig config,
            string artifactRoot,
            string agentJobId,
            int chunkIndex,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Test";
        public string ApplicationName { get; set; } = "Test";
        public string ContentRootPath { get; set; } = Path.GetTempPath();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class TestCallContext : ServerCallContext
    {
        protected override string MethodCore => "SubmitJob";
        protected override string HostCore => "localhost";
        protected override string PeerCore => "test";
        protected override DateTime DeadlineCore => DateTime.MaxValue;
        protected override Metadata RequestHeadersCore { get; } = new();
        protected override CancellationToken CancellationTokenCore => CancellationToken.None;
        protected override Metadata ResponseTrailersCore { get; } = new();
        protected override Status StatusCore { get; set; }
        protected override WriteOptions? WriteOptionsCore { get; set; }
        protected override AuthContext AuthContextCore => new(null, new Dictionary<string, List<AuthProperty>>());

        protected override ContextPropagationToken CreatePropagationTokenCore(ContextPropagationOptions? options) =>
            throw new NotSupportedException();

        protected override Task WriteResponseHeadersAsyncCore(Metadata responseHeaders) => Task.CompletedTask;
    }
}
