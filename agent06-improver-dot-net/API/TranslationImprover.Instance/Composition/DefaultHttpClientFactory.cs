namespace TranslationImprover.Composition;

/// <summary>
/// Simple IHttpClientFactory for use with Ninject when MS DI AddHttpClient is not merged into the kernel.
/// </summary>
public sealed class DefaultHttpClientFactory : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => new HttpClient();
}
