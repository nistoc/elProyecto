namespace Agent04.Features.Transcription.Application;

/// <summary>
/// Marks a business-logic method that updates the virtual model (RENTGEN).
/// When this method runs, the corresponding node operation (Ensure / Start / Complete) is reflected in the virtual model.
/// Used for documentation and for future interceptor-based updates; does not change execution order.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
public sealed class XRayNodeAttribute : Attribute
{
    public XRayNodeOperation Operation { get; set; }

    public XRayNodeAttribute(XRayNodeOperation operation)
    {
        Operation = operation;
    }
}

/// <summary>Node operation for the virtual model (aligns with INodeModel).</summary>
public enum XRayNodeOperation
{
    Ensure,
    Start,
    Complete
}
