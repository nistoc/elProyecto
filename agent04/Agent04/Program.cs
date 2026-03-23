using Agent04.Application;
using Agent04.Composition;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Logging.Console;

// Load .env from project directory so OPENAI_API_KEY is available
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

// gRPC over http:// (h2c): single endpoint HTTP/2 only (Windows без TLS).
builder.WebHost.ConfigureKestrel(serverOptions =>
{
    serverOptions.AllowAlternateSchemes = true;
    serverOptions.ListenLocalhost(5032, listenOptions => listenOptions.Protocols = HttpProtocols.Http2);
});

builder.Host.ConfigureHostOptions(o => o.ShutdownTimeout = TimeSpan.FromSeconds(5));

var openaiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
if (!string.IsNullOrEmpty(openaiKey))
    builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["openai_api_key"] = openaiKey });

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

builder.Services.AddMemoryCache();
builder.Services.AddAgent04Services(builder.Configuration);
builder.Services.AddHttpClient();
builder.Services.AddGrpc();

var app = builder.Build();

app.MapGrpcService<Agent04.Services.TranscriptionGrpcService>();

app.Run();
