using Microsoft.Extensions.Logging.Console;
using TranslationImprover.Application;
using TranslationImprover.Composition;

// Load .env from project directory so OPENAI_API_KEY is available (same as agent04)
var envPaths = new[]
{
    Path.Combine(Directory.GetCurrentDirectory(), ".env"),
    Path.Combine(AppContext.BaseDirectory, ".env")
};
foreach (var p in envPaths)
{
    if (File.Exists(p))
    {
        DotNetEnv.Env.Load(p);
        break;
    }
}

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(options =>
{
    options.SingleLine = true;
    options.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ ";
    options.UseUtcTimestamp = true;
});

// gRPC over http:// (h2c) needs an HTTP/2-only endpoint: cleartext Kestrel cannot negotiate HTTP/2
// next to HTTP/1.1 on one port, so agent05's gRPC calls failed with HTTP_1_1_REQUIRED.
// 5033 = gRPC (h2c, what agent05 dials), 5034 = REST / health / OpenAPI (HTTP/1.1).
builder.WebHost.ConfigureKestrel(serverOptions =>
{
    serverOptions.AllowAlternateSchemes = true;
    serverOptions.ListenLocalhost(5033, listenOptions => listenOptions.Protocols = Microsoft.AspNetCore.Server.Kestrel.Core.HttpProtocols.Http2);
    serverOptions.ListenLocalhost(5034, listenOptions => listenOptions.Protocols = Microsoft.AspNetCore.Server.Kestrel.Core.HttpProtocols.Http1);
});

// Graceful shutdown: при остановке (Ctrl+C) хост завершится за ShutdownTimeout и процесс освободит порт
builder.Host.ConfigureHostOptions(o => o.ShutdownTimeout = TimeSpan.FromSeconds(5));

// Expose OpenAI API key from .env for config (same as agent04)
var openaiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
if (!string.IsNullOrEmpty(openaiKey))
    builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["OpenAI:ApiKey"] = openaiKey });

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

builder.Services.AddHttpClient();
builder.Services.AddTranslationImproverServices();
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
