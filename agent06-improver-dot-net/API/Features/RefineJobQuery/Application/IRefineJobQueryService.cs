using TranslationImprover.Features.Refine.Application;
using TranslationImprover.Features.Refine.Domain;

namespace TranslationImprover.Features.RefineJobQuery.Application;

/// <summary>
/// Query refine jobs by semantic key and filters for virtual model / UI (0.01–10 Hz).
/// </summary>
public interface IRefineJobQueryService
{
    RefineJobStatus? GetById(string jobId);
    IReadOnlyList<RefineJobStatus> Query(RefineJobListFilter filter);

    IReadOnlyList<RefineJobStatus> QueryBySemanticKey(
        string semanticKey,
        RefineJobState? status = null,
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        int limit = 50,
        int offset = 0);
}
