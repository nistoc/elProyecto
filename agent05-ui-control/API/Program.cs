using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Console;
using XtractManager.Features.Jobs.Application;
using XtractManager.Features.Jobs.Infrastructure;
using XtractManager.Infrastructure;

// gRPC over plain http (no TLS) requires HTTP/2 "prior knowledge" (h2c); enable it so GrpcChannel can connect to Agent04/Agent06.
AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(options =>
{
    options.SingleLine = true;
    options.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ ";
    options.UseUtcTimestamp = true;
});

// Graceful shutdown: при остановке (Ctrl+C) хост завершится за ShutdownTimeout и процесс освободит порт
builder.Host.ConfigureHostOptions(o => o.ShutdownTimeout = TimeSpan.FromSeconds(5));

// DI: use built-in container (Ninject can be restored once IServiceScopeFactory conflict is resolved)
builder.Services.AddSingleton<InMemoryJobStore>();
builder.Services.AddSingleton<IJobStore>(sp => new WorkspaceAwareJobStore(
    sp.GetRequiredService<InMemoryJobStore>(),
    sp.GetRequiredService<XtractManager.Features.Jobs.Application.IJobWorkspace>(),
    sp.GetRequiredService<ILogger<WorkspaceAwareJobStore>>()));
builder.Services.AddSingleton<IBroadcaster, Broadcaster>();
builder.Services.AddSingleton<TranscriptionRefinerPipeline>();
builder.Services.AddSingleton<IPipeline>(sp => sp.GetRequiredService<TranscriptionRefinerPipeline>());
builder.Services.AddSingleton<IRefinerOrchestration>(sp => sp.GetRequiredService<TranscriptionRefinerPipeline>());
builder.Services.AddSingleton<ITranscriptionServiceClient, TranscriptionGrpcClient>();
builder.Services.AddSingleton<IRefinerServiceClient, RefinerGrpcClient>();
builder.Services.AddSingleton<XtractManager.Features.Jobs.Application.IJobWorkspace, JobWorkspace>();

builder.Services.AddControllers();
builder.Services.AddOpenApi();

var app = builder.Build();

WorkspaceParityChecker.ValidateAtStartup(
    app.Configuration,
    app.Services.GetRequiredService<IHostEnvironment>(),
    app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("XtractManager.Workspace"));

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.MapControllers();

app.MapGet("/health", () => Results.Ok(new { status = "healthy", service = "XtractManager" }));

app.Run();
