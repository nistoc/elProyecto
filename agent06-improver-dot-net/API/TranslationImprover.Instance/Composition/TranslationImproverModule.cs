using Ninject.Modules;
using TranslationImprover.Features.Refine.Application;
using TranslationImprover.Features.Refine.Infrastructure;
using TranslationImprover.Features.RefineJobQuery.Application;
using TranslationImprover.Features.RefineJobQuery.Infrastructure;

namespace TranslationImprover.Composition;

/// <summary>
/// Ninject composition root: Refine and RefineJobQuery bindings.
/// </summary>
public sealed class TranslationImproverModule : NinjectModule
{
    public override void Load()
    {
        Bind<IHttpClientFactory>().To<DefaultHttpClientFactory>().InSingletonScope();
        Bind<IRefineJobStore>().To<InMemoryRefineJobStore>().InSingletonScope();
        Bind<IRefineJobCancellation>().To<InMemoryRefineJobCancellation>().InSingletonScope();
        Bind<IOpenAIRefineClient>().To<OpenAIRefineClient>().InSingletonScope();
        Bind<IPromptLoader>().To<FilePromptLoader>().InSingletonScope();
        Bind<InMemoryNodeStore>().ToSelf().InSingletonScope();
        Bind<INodeModel>().To<InMemoryNodeStore>();
        Bind<INodeQuery>().To<InMemoryNodeStore>();
        Bind<IRefinePipeline>().To<RefinePipeline>().InSingletonScope();
        Bind<IRefineJobQueryService>().To<RefineJobQueryService>().InSingletonScope();
    }
}
