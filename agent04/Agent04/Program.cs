using Agent04.Application;
using Agent04.Composition;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Ninject;
using Ninject.Extensions.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);

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

// Composition root: Ninject module with all Transcription feature bindings
builder.Host
    .UseServiceProviderFactory(new NinjectServiceProviderFactory())
    .ConfigureContainer<IKernel>(kernel => kernel.Load<Agent04Module>());

builder.Services.AddMemoryCache();
builder.Services.AddHttpClient();
builder.Services.AddRateLimiter(options =>
{
    options.AddFixedWindowLimiter("api", opt =>
    {
        opt.PermitLimit = builder.Configuration.GetValue("RateLimit:PermitLimit", 100);
        opt.Window = TimeSpan.FromSeconds(builder.Configuration.GetValue("RateLimit:WindowSeconds", 60));
    });
});
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
                Description = "RENTGEN virtual abstract model: query jobs by semantic key (GET /jobs/query?semanticKey=...); get node tree per job (GET /jobs/{id}/nodes; optional ?tag=nodeId for single node status). scopeId = jobId. Designed for 0.01–10 Hz polling. See docs/RENTGEN_IMPLEMENTATION.md."
            });
        if (document.Tags.All(t => t.Name != "Jobs"))
            document.Tags.Add(new Microsoft.OpenApi.Models.OpenApiTag { Name = "Jobs", Description = "Submit and monitor transcription jobs." });
        return System.Threading.Tasks.Task.CompletedTask;
    });
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.UseRateLimiter();

app.MapGet("/", () => Results.Ok(new { service = "Agent04", status = "running" }));
app.MapControllers();
app.MapGrpcService<Agent04.Services.TranscriptionGrpcService>();

app.Run();
