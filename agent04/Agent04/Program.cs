using Agent04.Application;
using Agent04.Composition;
using Agent04.Features.Transcription.Application;
using Agent04.Features.Transcription.Domain;
using Microsoft.Extensions.Configuration;
using Ninject;
using Ninject.Extensions.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

// Config path: --config=<path> / --config <path> for CLI, else config/default.json
var (configPathFromArgs, isCliMode) = GetConfigPathAndMode(args, builder.Environment.ContentRootPath);

// Workspace root is required: read from appsettings / env, validate at startup
var workspaceRootRaw = builder.Configuration["WorkspaceRoot"] ?? builder.Configuration["workspace_root"] ?? "";
if (string.IsNullOrWhiteSpace(workspaceRootRaw))
{
    Console.Error.WriteLine("WorkspaceRoot (or workspace_root) is required in appsettings.json or environment. Application will exit.");
    Environment.Exit(1);
}
var workspaceRootFull = Path.GetFullPath(workspaceRootRaw.Trim());
if (!Directory.Exists(workspaceRootFull))
{
    Console.Error.WriteLine($"Workspace root directory does not exist: {workspaceRootFull}. Application will exit.");
    Environment.Exit(1);
}
builder.Services.AddSingleton(new WorkspaceRoot(workspaceRootFull));

// CLI: resolve config path relative to workspace root; web: use default path (not loaded here)
string resolvedConfigPath = configPathFromArgs;
if (isCliMode && !string.IsNullOrEmpty(configPathFromArgs))
{
    var relative = configPathFromArgs.TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    resolvedConfigPath = Path.Combine(workspaceRootFull, relative);
}
if (File.Exists(resolvedConfigPath))
    builder.Configuration.AddJsonFile(resolvedConfigPath, optional: !isCliMode, reloadOnChange: false);

// Composition root: Ninject module with all Transcription feature bindings
builder.Host
    .UseServiceProviderFactory(new NinjectServiceProviderFactory())
    .ConfigureContainer<IKernel>(kernel => kernel.Load<Agent04Module>());

builder.Services.AddMemoryCache();
builder.Services.AddControllers();
builder.Services.AddGrpc();
builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer((document, context, cancel) =>
    {
        document.Tags ??= new List<Microsoft.OpenApi.Models.OpenApiTag>();
        if (document.Tags.All(t => t.Name != "Virtual model (RENTGEN)"))
            document.Tags.Add(new Microsoft.OpenApi.Models.OpenApiTag
            {
                Name = "Virtual model (RENTGEN)",
                Description = "RENTGEN virtual abstract model: query jobs by semantic key (tags), get hierarchical node tree per job (scopeId = jobId). Use GET /jobs/query for list with filters; GET /jobs/{id}/nodes for step/chunk hierarchy. Designed for 0.01–10 Hz polling. See docs/RENTGEN_IMPLEMENTATION.md."
            });
        if (document.Tags.All(t => t.Name != "Jobs"))
            document.Tags.Add(new Microsoft.OpenApi.Models.OpenApiTag { Name = "Jobs", Description = "Submit and monitor transcription jobs." });
        return System.Threading.Tasks.Task.CompletedTask;
    });
});

var app = builder.Build();

if (isCliMode)
{
    await RunCliAsync(app.Services, resolvedConfigPath, workspaceRootFull);
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

static async Task RunCliAsync(IServiceProvider services, string configPath, string workspaceRoot)
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
        var relative = file.TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var inputPath = Path.Combine(workspaceRoot, relative);
        if (!File.Exists(inputPath))
        {
            Console.Error.WriteLine($"Input file not found: {inputPath}");
            Environment.Exit(1);
        }
        var (mdPath, jsonPath) = await pipeline.ProcessFileAsync(config, inputPath, workspaceRoot, null, null, null);
        Console.WriteLine($"Markdown: {mdPath}");
        Console.WriteLine($"JSON: {jsonPath}");
    }
}
