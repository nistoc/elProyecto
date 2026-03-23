namespace TranslationImprover.Features.Refine.Domain;

public enum RefineJobState
{
    Pending,
    Running,
    Paused,
    Completed,
    Failed,
    Cancelled
}
