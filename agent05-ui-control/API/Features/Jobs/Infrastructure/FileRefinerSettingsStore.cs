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
    private const string SystemPromptFileName = "refiner_system_prompt.txt";
    private readonly IJobWorkspace _workspace;
    private readonly object _gate = new();

    public FileRefinerSettingsStore(IJobWorkspace workspace) => _workspace = workspace;

    private string PromptPath => Path.Combine(_workspace.WorkspaceRootPath, IRefinerSettingsStore.PromptFileName);
    private string SettingsPath => Path.Combine(_workspace.WorkspaceRootPath, SettingsFileName);
    private string SystemPromptPath => Path.Combine(_workspace.WorkspaceRootPath, SystemPromptFileName);

    public RefinerSettings Load()
    {
        lock (_gate)
        {
            var prompt = ReadOptionalText(PromptPath);
            var systemPrompt = ReadOptionalText(SystemPromptPath);

            Stored? stored = null;
            if (File.Exists(SettingsPath))
            {
                try { stored = JsonSerializer.Deserialize<Stored>(File.ReadAllText(SettingsPath, Encoding.UTF8)); }
                catch (JsonException) { /* corrupt file → defaults */ }
            }

            return new RefinerSettings(prompt, stored?.BatchSize, stored?.ContextLines,
                string.IsNullOrWhiteSpace(stored?.Model) ? null : stored!.Model, systemPrompt);
        }
    }

    public void Save(RefinerSettings settings)
    {
        lock (_gate)
        {
            Directory.CreateDirectory(_workspace.WorkspaceRootPath);
            WriteOptionalText(PromptPath, settings.PromptTemplate);
            WriteOptionalText(SystemPromptPath, settings.SystemPrompt);

            var json = JsonSerializer.Serialize(new Stored(settings.BatchSize, settings.ContextLines, settings.Model),
                new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(SettingsPath, json, new UTF8Encoding(false));
        }
    }

    private static string? ReadOptionalText(string path)
    {
        if (!File.Exists(path)) return null;
        var text = File.ReadAllText(path, Encoding.UTF8);
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    /// <summary>Null/blank deletes the file, so the built-in prompt applies again.</summary>
    private static void WriteOptionalText(string path, string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            if (File.Exists(path)) File.Delete(path);
        }
        else
        {
            File.WriteAllText(path, text, new UTF8Encoding(false));
        }
    }

    private sealed record Stored(int? BatchSize, int? ContextLines, string? Model);
}
