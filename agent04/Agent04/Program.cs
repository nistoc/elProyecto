using Agent04.Features.Transcription.Application;
using Agent04.Features.Transcription.Domain;
using Agent04.Features.Transcription.Infrastructure;
using Microsoft.Extensions.Configuration;

var builder = WebApplication.CreateBuilder(args);

// Config path: --config=<path> / --config <path> for CLI, else config/default.json
var (configPath, isCliMode) = GetConfigPathAndMode(args, builder.Environment.ContentRootPath);
if (File.Exists(configPath))
    builder.Configuration.AddJsonFile(configPath, optional: !isCliMode, reloadOnChange: false);

builder.Services.AddSingleton<IAudioUtils, AudioUtils>();
builder.Services.AddTransient<IAudioChunker>(sp =>
{
    var utils = sp.GetRequiredService<IAudioUtils>();
    var config = sp.GetRequiredService<IConfiguration>();
    var ffmpeg = utils.WhichOr(config["ffmpeg_path"], "ffmpeg") ?? "ffmpeg";
    var ffprobe = utils.WhichOr(config["ffprobe_path"], "ffprobe") ?? "ffprobe";
    return new AudioChunker(utils, ffmpeg, ffprobe);
});
builder.Services.AddSingleton<ITranscriptionCache>(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var cacheDir = config["cache_dir"] ?? "cache";
    var logger = sp.GetService<Microsoft.Extensions.Logging.ILogger<TranscriptionCache>>();
    return new TranscriptionCache(cacheDir, logger);
});
builder.Services.AddSingleton<ITranscriptionMerger, TranscriptionMerger>();
builder.Services.AddSingleton<ITranscriptionPipeline, TranscriptionPipeline>();
builder.Services.AddSingleton<IJobStatusStore, InMemoryJobStatusStore>();
builder.Services.AddSingleton<IJobQueryService, JobQueryService>();
builder.Services.AddControllers();
builder.Services.AddGrpc();
builder.Services.AddSingleton<ICancellationManager>(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var cancelDir = config["cancel_dir"] ?? "cancel_signals";
    return new CancellationManager(cancelDir);
});
builder.Services.AddSingleton<ITranscriptionOutputWriter>(sp =>
{
    var logger = sp.GetService<Microsoft.Extensions.Logging.ILogger<TranscriptionOutputWriter>>();
    return new TranscriptionOutputWriter(logger);
});
builder.Services.AddSingleton<ITranscriptionClient>(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
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
    var logger = sp.GetService<Microsoft.Extensions.Logging.ILogger<OpenAITranscriptionClient>>();
    return new OpenAITranscriptionClient(http, apiKey, model, fallback, logger);
});
builder.Services.AddOpenApi();

var app = builder.Build();

if (isCliMode)
{
    await RunCliAsync(app.Services, configPath);
    return;
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.MapGet("/", () => Results.Ok(new { service = "Agent04", status = "running" }));
app.MapControllers();
app.MapGrpcService<Agent04.Services.TranscriptionGrpcService>();

app.Run();

static (string path, bool isCli) GetConfigPathAndMode(string[] args, string contentRoot)
{
    for (var i = 0; i < args.Length; i++)
    {
        if (args[i].StartsWith("--config=", StringComparison.Ordinal))
        {
            var p = args[i].Substring("--config=".Length).Trim();
            return (p.Length > 0 ? p : Path.Combine(contentRoot, "config", "default.json"), true);
        }
        if (args[i] == "--config" && i + 1 < args.Length)
            return (args[i + 1].Trim(), true);
    }
    return (Path.Combine(contentRoot, "config", "default.json"), false);
}

static async Task RunCliAsync(IServiceProvider services, string configPath)
{
    if (!File.Exists(configPath))
    {
        Console.Error.WriteLine($"Config file not found: {configPath}");
        Environment.Exit(1);
    }
    var config = await TranscriptionConfig.FromFileAsync(configPath);
    var files = config.GetFiles();
    if (files.Count == 0)
    {
        Console.Error.WriteLine("No input files in config (file / files).");
        Environment.Exit(1);
    }
    var pipeline = services.GetRequiredService<ITranscriptionPipeline>();
    foreach (var file in files)
    {
        var inputPath = file;
        if (!Path.IsPathRooted(inputPath))
            inputPath = Path.Combine(Path.GetDirectoryName(configPath) ?? ".", inputPath);
        if (!File.Exists(inputPath))
        {
            Console.Error.WriteLine($"Input file not found: {inputPath}");
            Environment.Exit(1);
        }
        var (mdPath, jsonPath) = await pipeline.ProcessFileAsync(config, inputPath);
        Console.WriteLine($"Markdown: {mdPath}");
        Console.WriteLine($"JSON: {jsonPath}");
    }
}
