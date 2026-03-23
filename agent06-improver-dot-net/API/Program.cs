using Microsoft.AspNetCore.Server.Kestrel.Core;
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

// gRPC over http:// (h2c): same pattern as Agent04 — explicit HTTP/2 only on 5033.
// Default Kestrel (Http1AndHttp2 from launchSettings) can break GrpcChannel + Http2UnencryptedSupport (HTTP_1_1_REQUIRED, RedirectHandler).
// Port busy: stop the other listener (often a previous run). Windows: netstat -ano | findstr :5033  then  taskkill /PID <pid> /F
builder.WebHost.ConfigureKestrel(serverOptions =>
{
    serverOptions.AllowAlternateSchemes = true;
    serverOptions.ListenLocalhost(5033, listenOptions =>
    {
        listenOptions.Protocols = HttpProtocols.Http2;
    });
});

// Graceful shutdown (same idea as Agent04 host tuning; optional Agent06:ShutdownTimeoutSeconds)
var shutdownSec = builder.Configuration.GetValue("Agent06:ShutdownTimeoutSeconds", 5);
if (shutdownSec < 1)
    shutdownSec = 5;
builder.Host.ConfigureHostOptions(o => o.ShutdownTimeout = TimeSpan.FromSeconds(shutdownSec));

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
builder.Services.AddGrpc(o =>
{
    o.MaxReceiveMessageSize = 32 * 1024 * 1024;
    o.MaxSendMessageSize = 32 * 1024 * 1024;
});
builder.Services.AddOpenApi();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// Do not call UseHttpsRedirection: Agent05 connects with GrpcChannel to http://localhost:5033 (h2c). A redirect to
// HTTPS makes the client follow with RedirectHandler and breaks HTTP/2 (RpcException HTTP_1_1_REQUIRED).

app.MapGet("/", () => Results.Ok(new { service = "TranslationImprover", status = "running" }));
app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));
app.MapControllers();
app.MapGrpcService<TranslationImprover.Services.RefinerGrpcService>();

app.Run();
