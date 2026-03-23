namespace TranslationImprover.Features.Refine.Application;

/// <summary>Cooperative pause: pipeline stops after the current batch completes.</summary>
public interface IRefineJobPause
{
    void RequestPause(string jobId);
    bool IsPauseRequested(string jobId);
    void ClearPauseRequest(string jobId);
}
