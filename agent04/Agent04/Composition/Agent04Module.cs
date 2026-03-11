using Agent04.Features.Transcription.Application;
using Agent04.Features.Transcription.Infrastructure;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Ninject;
using Ninject.Modules;
using Ninject.Syntax;

namespace Agent04.Composition;

/// <summary>
/// Ninject composition root: all Feature/Transcription bindings (pipeline, store, cache, OpenAI client, etc.).
/// Framework services (IConfiguration, IMemoryCache, ILogger) are provided by the host and merged into the kernel.
/// </summary>
public sealed class Agent04Module : NinjectModule
{
    public override void Load()
    {
        Bind<IAudioUtils>().To<AudioUtils>().InSingletonScope();

        Bind<IAudioChunker>().ToMethod(ctx =>
        {
            var k = ctx.Kernel as IResolutionRoot ?? throw new InvalidOperationException("Kernel is not IResolutionRoot");
            var utils = k.Get<IAudioUtils>();
            var config = k.Get<IConfiguration>();
            var ffmpeg = utils.WhichOr(config["ffmpeg_path"], "ffmpeg") ?? "ffmpeg";
            var ffprobe = utils.WhichOr(config["ffprobe_path"], "ffprobe") ?? "ffprobe";
            return new AudioChunker(utils, ffmpeg, ffprobe);
        }).InSingletonScope();

        Bind<ITranscriptionCache>().ToMethod(ctx =>
        {
            var k = ctx.Kernel as IResolutionRoot ?? throw new InvalidOperationException("Kernel is not IResolutionRoot");
            var config = k.Get<IConfiguration>();
            var cacheDir = config["cache_dir"] ?? "cache";
            var logger = k.Get<ILoggerFactory>().CreateLogger<TranscriptionCache>();
            return new TranscriptionCache(cacheDir, logger);
        }).InSingletonScope();

        Bind<ITranscriptionMerger>().To<TranscriptionMerger>().InSingletonScope();
        Bind<ITranscriptionPipeline>().To<TranscriptionPipeline>().InSingletonScope();

        Bind<InMemoryJobStatusStore>().ToSelf().InSingletonScope();
        Bind<IJobStatusStore>().ToMethod(ctx =>
        {
            var k = ctx.Kernel as IResolutionRoot ?? throw new InvalidOperationException("Kernel is not IResolutionRoot");
            return new CachingJobStatusStore(k.Get<InMemoryJobStatusStore>(), k.Get<IMemoryCache>());
        })
            .InSingletonScope();

        Bind<IJobQueryService>().To<JobQueryService>().InSingletonScope();

        Bind<InMemoryNodeStore>().ToSelf().InSingletonScope();
        Bind<INodeModel>().ToMethod(ctx => ctx.Kernel.Get<InMemoryNodeStore>()).InSingletonScope();
        Bind<INodeQuery>().ToMethod(ctx => ctx.Kernel.Get<InMemoryNodeStore>()).InSingletonScope();

        Bind<ICancellationManager>().ToMethod(ctx =>
        {
            var k = ctx.Kernel as IResolutionRoot ?? throw new InvalidOperationException("Kernel is not IResolutionRoot");
            var config = k.Get<IConfiguration>();
            var cancelDir = config["cancel_dir"] ?? "cancel_signals";
            return new CancellationManager(cancelDir);
        }).InSingletonScope();

        Bind<ITranscriptionOutputWriter>().ToMethod(ctx =>
        {
            var k = ctx.Kernel as IResolutionRoot ?? throw new InvalidOperationException("Kernel is not IResolutionRoot");
            var logger = k.Get<ILoggerFactory>().CreateLogger<TranscriptionOutputWriter>();
            return new TranscriptionOutputWriter(logger);
        }).InSingletonScope();

        Bind<ITranscriptionClient>().ToMethod(ctx =>
        {
            var k = ctx.Kernel as IResolutionRoot ?? throw new InvalidOperationException("Kernel is not IResolutionRoot");
            var config = k.Get<IConfiguration>();
            var apiKey = config["openai_api_key"]?.ToString() ?? "";
            if (string.IsNullOrEmpty(apiKey))
                throw new InvalidOperationException("openai_api_key not configured");
            var baseUrl = config["openai_base_url"]?.ToString();
            var timeoutSec = config.GetValue<int>("api_timeout_seconds", 240);
            var http = new HttpClient();
            http.BaseAddress = new Uri(string.IsNullOrEmpty(baseUrl) ? "https://api.openai.com/" : baseUrl.TrimEnd('/') + "/");
            http.Timeout = TimeSpan.FromSeconds(timeoutSec);
            var model = config["model"]?.ToString() ?? "gpt-4o-transcribe-diarize";
            var fallback = config.GetSection("fallback_models").Get<string[]>() ?? new[] { "gpt-4o-mini-transcribe", "whisper-1" };
            var logger = k.Get<ILoggerFactory>().CreateLogger<OpenAITranscriptionClient>();
            return new OpenAITranscriptionClient(http, apiKey, model, fallback, logger);
        }).InSingletonScope();
    }
}
