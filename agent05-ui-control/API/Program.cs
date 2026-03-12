using Microsoft.AspNetCore.Mvc;
using XtractManager.Features.Jobs.Application;
using XtractManager.Features.Jobs.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// Graceful shutdown: при остановке (Ctrl+C) хост завершится за ShutdownTimeout и процесс освободит порт
builder.Host.ConfigureHostOptions(o => o.ShutdownTimeout = TimeSpan.FromSeconds(5));

// DI: use built-in container (Ninject can be restored once IServiceScopeFactory conflict is resolved)
builder.Services.AddSingleton<InMemoryJobStore>();
builder.Services.AddSingleton<IJobStore>(sp => new WorkspaceAwareJobStore(
    sp.GetRequiredService<InMemoryJobStore>(),
    sp.GetRequiredService<XtractManager.Features.Jobs.Application.IJobWorkspace>(),
    sp.GetRequiredService<ILogger<WorkspaceAwareJobStore>>()));
builder.Services.AddSingleton<IBroadcaster, Broadcaster>();
builder.Services.AddSingleton<IPipeline, TranscriptionRefinerPipeline>();
builder.Services.AddSingleton<ITranscriptionServiceClient, TranscriptionGrpcClient>();
builder.Services.AddSingleton<IRefinerServiceClient, RefinerGrpcClient>();
builder.Services.AddSingleton<XtractManager.Features.Jobs.Application.IJobWorkspace, JobWorkspace>();

builder.Services.AddControllers();
builder.Services.AddOpenApi();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.MapControllers();

app.MapGet("/health", () => Results.Ok(new { status = "healthy", service = "XtractManager" }));

app.Run();
