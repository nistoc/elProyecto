using Microsoft.Extensions.DependencyInjection;
using TranslationImprover.Features.Refine.Application;
using TranslationImprover.Features.Refine.Infrastructure;
using TranslationImprover.Features.RefineJobQuery.Application;
using TranslationImprover.Features.RefineJobQuery.Infrastructure;

namespace TranslationImprover.Composition;

/// <summary>
/// Registers all TranslationImprover feature services with Microsoft.Extensions.DependencyInjection.
/// </summary>
public static class TranslationImproverServiceRegistration
{
    public static IServiceCollection AddTranslationImproverServices(this IServiceCollection services)
    {
        services.AddSingleton<IRefineJobStore, InMemoryRefineJobStore>();
        services.AddSingleton<IRefineJobCancellation, InMemoryRefineJobCancellation>();
        services.AddSingleton<IOpenAIRefineClient, OpenAIRefineClient>();
        services.AddSingleton<IPromptLoader, FilePromptLoader>();

        services.AddSingleton<InMemoryNodeStore>();
        services.AddSingleton<INodeModel>(sp => sp.GetRequiredService<InMemoryNodeStore>());
        services.AddSingleton<INodeQuery>(sp => sp.GetRequiredService<InMemoryNodeStore>());

        services.AddSingleton<IRefinePipeline, RefinePipeline>();
        services.AddSingleton<IRefineJobQueryService, RefineJobQueryService>();

        return services;
    }
}
