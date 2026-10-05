using System.Text;
using System.Text.Json;
using XtractManager.Features.Jobs.Application;

namespace XtractManager.Features.Jobs.Infrastructure;

/// <summary>
/// Stores the prompt template as plain text (<c>refiner_prompt.txt</c>, easy to diff and edit by hand)
/// and the numeric settings in <c>refiner_settings.json</c>, both in the workspace root.
/// </summary>
public sealed class FileRefinerSettingsStore : IRefinerSettingsStore
{
    private const string SettingsFileName = "refiner_settings.json";
    private readonly IJobWorkspace _workspace;
    private readonly object _gate = new();

    public FileRefinerSettingsStore(IJobWorkspace workspace) => _workspace = workspace;

    private string PromptPath => Path.Combine(_workspace.WorkspaceRootPath, IRefinerSettingsStore.PromptFileName);
    private string SettingsPath => Path.Combine(_workspace.WorkspaceRootPath, SettingsFileName);

    public RefinerSettings Load()
    {
        lock (_gate)
        {
            string? prompt = null;
            if (File.Exists(PromptPath))
            {
                var text = File.ReadAllText(PromptPath, Encoding.UTF8);
                prompt = string.IsNullOrWhiteSpace(text) ? null : text;
            }

            Stored? stored = null;
            if (File.Exists(SettingsPath))
            {
                try { stored = JsonSerializer.Deserialize<Stored>(File.ReadAllText(SettingsPath, Encoding.UTF8)); }
                catch (JsonException) { /* corrupt file → defaults */ }
            }

            return new RefinerSettings(prompt, stored?.BatchSize, stored?.ContextLines,
                string.IsNullOrWhiteSpace(stored?.Model) ? null : stored!.Model);
        }
    }

    public void Save(RefinerSettings settings)
    {
        lock (_gate)
        {
            Directory.CreateDirectory(_workspace.WorkspaceRootPath);
            if (string.IsNullOrWhiteSpace(settings.PromptTemplate))
            {
                if (File.Exists(PromptPath)) File.Delete(PromptPath); // back to the built-in prompt
            }
            else
            {
                File.WriteAllText(PromptPath, settings.PromptTemplate, new UTF8Encoding(false));
            }

            var json = JsonSerializer.Serialize(new Stored(settings.BatchSize, settings.ContextLines, settings.Model),
                new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(SettingsPath, json, new UTF8Encoding(false));
        }
    }

    private sealed record Stored(int? BatchSize, int? ContextLines, string? Model);
}
