using Agent04.Composition;
using Agent04.Features.Transcription.Application;
using Agent04.Features.Transcription.Domain;
using Microsoft.Extensions.Configuration;
using Ninject;
using Ninject.Extensions.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

// Config path: --config=<path> / --config <path> for CLI, else config/default.json
var (configPath, isCliMode) = GetConfigPathAndMode(args, builder.Environment.ContentRootPath);
if (File.Exists(configPath))
    builder.Configuration.AddJsonFile(configPath, optional: !isCliMode, reloadOnChange: false);

// Composition root: Ninject module with all Transcription feature bindings
builder.Host
    .UseServiceProviderFactory(new NinjectServiceProviderFactory())
    .ConfigureContainer<IKernel>(kernel => kernel.Load<Agent04Module>());

builder.Services.AddMemoryCache();
builder.Services.AddControllers();
builder.Services.AddGrpc();
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
