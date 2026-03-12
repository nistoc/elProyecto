namespace XtractManager.Features.Jobs.Application;

public interface IPipeline
{
    Task RunAsync(string jobId, CancellationToken ct = default);
}
