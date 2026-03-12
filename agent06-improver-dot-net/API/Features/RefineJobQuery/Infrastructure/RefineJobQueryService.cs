using TranslationImprover.Features.Refine.Application;
using TranslationImprover.Features.Refine.Domain;
using TranslationImprover.Features.RefineJobQuery.Application;

namespace TranslationImprover.Features.RefineJobQuery.Infrastructure;

public sealed class RefineJobQueryService : IRefineJobQueryService
{
    private readonly IRefineJobStore _store;

    public RefineJobQueryService(IRefineJobStore store)
    {
        _store = store;
    }

    public RefineJobStatus? GetById(string jobId) => _store.Get(jobId);

    public IReadOnlyList<RefineJobStatus> Query(RefineJobListFilter filter) => _store.List(filter);

    public IReadOnlyList<RefineJobStatus> QueryBySemanticKey(
        string semanticKey,
        RefineJobState? status = null,
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        int limit = 50,
        int offset = 0)
    {
        var filter = new RefineJobListFilter
        {
            SemanticKey = semanticKey,
            Status = status,
            From = from,
            To = to,
            Limit = limit,
            Offset = offset
        };
        return _store.List(filter);
    }
}
