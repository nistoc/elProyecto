using Agent04.Features.JobQuery.Application;
using Agent04.Features.JobQuery.Infrastructure;
using Agent04.Features.Transcription.Application;
using Agent04.Features.Transcription.Infrastructure;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Agent04.Composition;

/// <summary>
/// Registers all Agent04 feature services with the standard Microsoft.Extensions.DependencyInjection container.
/// </summary>
public static class Agent04ServiceRegistration
{
    public static IServiceCollection AddAgent04Services(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IAudioUtils, AudioUtils>();

        services.AddSingleton<IAudioChunker>(sp =>
        {
            var utils = sp.GetRequiredService<IAudioUtils>();
            var config = sp.GetRequiredService<IConfiguration>();
            var ffmpeg = utils.WhichOr(config["ffmpeg_path"], "ffmpeg") ?? "ffmpeg";
            var ffprobe = utils.WhichOr(config["ffprobe_path"], "ffprobe") ?? "ffprobe";
            return new AudioChunker(utils, ffmpeg, ffprobe);
        });

        services.AddSingleton<ITranscriptionCache>(sp =>
        {
            var config = sp.GetRequiredService<IConfiguration>();
            var cacheDir = config["cache_dir"] ?? "cache";
            var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger<TranscriptionCache>();
            return new TranscriptionCache(cacheDir, logger);
        });

        services.AddSingleton<ITranscriptionMerger, TranscriptionMerger>();
        services.AddSingleton<ITranscriptionPipeline, TranscriptionPipeline>();
        services.AddSingleton<IOutboundJobNotifier, NoOpOutboundJobNotifier>();

        services.AddSingleton<InMemoryJobStatusStore>();
        services.AddSingleton<IJobStatusStore>(sp =>
            new CachingJobStatusStore(
                sp.GetRequiredService<InMemoryJobStatusStore>(),
                sp.GetRequiredService<IMemoryCache>()));

        services.AddSingleton<IJobQueryService, JobQueryService>();

        services.AddSingleton<InMemoryNodeStore>();
        services.AddSingleton<INodeModel>(sp => sp.GetRequiredService<InMemoryNodeStore>());
        services.AddSingleton<INodeQuery>(sp => sp.GetRequiredService<InMemoryNodeStore>());

        services.AddSingleton<ICancellationManagerFactory, PerJobCancellationManagerFactory>();

        services.AddSingleton<ITranscriptionOutputWriter>(sp =>
        {
            var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger<TranscriptionOutputWriter>();
            return new TranscriptionOutputWriter(logger);
        });

        services.AddSingleton<ITranscriptionClient>(sp =>
        {
            var config = sp.GetRequiredService<IConfiguration>();
            var apiKey = config["openai_api_key"]?.ToString() ?? "";
            if (string.IsNullOrEmpty(apiKey))
                throw new InvalidOperationException("openai_api_key not configured");
            var baseUrl = config["openai_base_url"]?.ToString();
            var timeoutSec = config.GetValue("api_timeout_seconds", 240);
            var http = new HttpClient();
            http.BaseAddress = new Uri(string.IsNullOrEmpty(baseUrl) ? "https://api.openai.com/" : baseUrl.TrimEnd('/') + "/");
            http.Timeout = TimeSpan.FromSeconds(timeoutSec);
            var model = config["model"]?.ToString() ?? "gpt-4o-transcribe-diarize";
            var fallback = config.GetSection("fallback_models").Get<string[]>() ?? new[] { "gpt-4o-mini-transcribe", "whisper-1" };
            var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger<OpenAITranscriptionClient>();
            return new OpenAITranscriptionClient(http, apiKey, model, fallback, logger);
        });

        return services;
    }
}
