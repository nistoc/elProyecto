namespace TranslationImprover.Features.Refine.Domain;

public enum RefineJobState
{
    Pending,
    Running,
    Completed,
    Failed,
    Cancelled
}
