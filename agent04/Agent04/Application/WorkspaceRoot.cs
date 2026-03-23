namespace Agent04.Application;

/// <summary>
/// Absolute path to the workspace root (sandbox). Required; validated at startup.
/// </summary>
public sealed class WorkspaceRoot
{
    public string RootPath { get; }

    public WorkspaceRoot(string rootPath)
    {
        RootPath = rootPath;
    }
}
