using Ninject.Modules;
using XtractManager.Features.Jobs.Application;
using XtractManager.Features.Jobs.Infrastructure;

namespace XtractManager.Composition;

public sealed class XtractManagerInstanceModule : NinjectModule
{
    public override void Load()
    {
        Bind<IJobStore>().To<InMemoryJobStore>().InSingletonScope();
        Bind<IBroadcaster>().To<Broadcaster>().InSingletonScope();
        Bind<IPipeline>().To<StubPipeline>().InSingletonScope();
        Bind<ITranscriptionServiceClient>().To<StubTranscriptionServiceClient>().InSingletonScope();
        Bind<IRefinerServiceClient>().To<StubRefinerServiceClient>().InSingletonScope();
    }
}
