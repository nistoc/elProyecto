using Microsoft.Extensions.Configuration;
using Ninject;
using Ninject.Extensions.DependencyInjection;
using TranslationImprover.Application;
using TranslationImprover.Composition;

// Load .env into environment (like agent03); .env is gitignored
LoadEnvFile();

var builder = WebApplication.CreateBuilder(args);

static void LoadEnvFile()
{
    var dirs = new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory };
    foreach (var dir in dirs)
    {
        if (string.IsNullOrEmpty(dir)) continue;
        var path = Path.Combine(dir, ".env");
        if (!System.IO.File.Exists(path)) continue;
        foreach (var line in System.IO.File.ReadLines(path))
        {
            var s = line.Trim();
            if (s.Length == 0 || s[0] == '#') continue;
            var eq = s.IndexOf('=');
            if (eq <= 0) continue;
            var key = s[0..eq].Trim();
            var value = s[(eq + 1)..].Trim();
            if (string.IsNullOrEmpty(key)) continue;
            if (value.StartsWith('"') && value.EndsWith('"') && value.Length >= 2)
                value = value[1..^1].Replace("\\\"", "\"");
            Environment.SetEnvironmentVariable(key, value, EnvironmentVariableTarget.Process);
            if (string.Equals(key, "OPENAI_API_KEY", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(value))
                Environment.SetEnvironmentVariable("OpenAI__ApiKey", value, EnvironmentVariableTarget.Process);
        }
        break;
    }
}

// Workspace root is required (appsettings / env); validate at startup
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

// Composition root: Ninject
builder.Host
    .UseServiceProviderFactory(new NinjectServiceProviderFactory())
    .ConfigureContainer<IKernel>((context, kernel) =>
    {
        kernel.Bind<IConfiguration>().ToConstant(context.Configuration);
        kernel.Load<TranslationImproverModule>();
    });

builder.Services.AddHttpClient();
builder.Services.AddControllers();
builder.Services.AddGrpc();
builder.Services.AddOpenApi();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.MapGet("/", () => Results.Ok(new { service = "TranslationImprover", status = "running" }));
app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));
app.MapControllers();
app.MapGrpcService<TranslationImprover.Services.RefinerGrpcService>();

app.Run();
