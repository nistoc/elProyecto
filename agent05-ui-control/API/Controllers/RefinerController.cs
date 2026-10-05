using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using XtractManager.Features.Jobs.Application;
using XtractManager.Features.Jobs.Infrastructure;

namespace XtractManager.Controllers;

/// <summary>
/// Refiner tuning (prompt template, batch geometry, model) shared by all jobs, and the saved
/// per-batch history of a job's last refiner run.
/// </summary>
[ApiController]
public class RefinerController : ControllerBase
{
    private readonly IRefinerSettingsStore _settings;
    private readonly IRefinerServiceClient _refiner;
    private readonly IJobWorkspace _workspace;
    private readonly ILogger<RefinerController> _logger;

    public RefinerController(IRefinerSettingsStore settings, IRefinerServiceClient refiner, IJobWorkspace workspace, ILogger<RefinerController> logger)
    {
        _settings = settings;
        _refiner = refiner;
        _workspace = workspace;
        _logger = logger;
    }

    /// <summary>Effective settings plus the built-in defaults (from agent06) so the UI can show and reset them.</summary>
    [HttpGet("api/refiner/settings")]
    public async Task<IActionResult> GetSettings(CancellationToken ct = default)
    {
        RefineDefaults defaults;
        try
        {
            defaults = await _refiner.GetDefaultsAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Agent06 GetRefineDefaults failed");
            return StatusCode(502, new { error = "refiner service (agent06) is unavailable: " + ex.Message });
        }

        var s = _settings.Load();
        return Ok(new
        {
            promptTemplate = s.PromptTemplate ?? defaults.UserPromptTemplate,
            isCustomPrompt = s.PromptTemplate != null,
            defaultPromptTemplate = defaults.UserPromptTemplate,
            systemPrompt = defaults.SystemPrompt,
            batchSize = s.BatchSize ?? IRefinerSettingsStore.DefaultBatchSize,
            contextLines = s.ContextLines ?? IRefinerSettingsStore.DefaultContextLines,
            model = s.Model ?? IRefinerSettingsStore.DefaultModel,
            defaults = new
            {
                batchSize = IRefinerSettingsStore.DefaultBatchSize,
                contextLines = IRefinerSettingsStore.DefaultContextLines,
                model = IRefinerSettingsStore.DefaultModel
            }
        });
    }

    /// <summary>
    /// Saves settings. <c>promptTemplate</c> null/empty or equal to the default → built-in prompt is used.
    /// The template must keep the <c>{batch}</c> placeholder (the lines to fix are inserted there).
    /// </summary>
    [HttpPut("api/refiner/settings")]
    public async Task<IActionResult> PutSettings([FromBody] RefinerSettingsRequest body, CancellationToken ct = default)
    {
        if (body.BatchSize is < 1 or > 200)
            return BadRequest(new { error = "batchSize must be between 1 and 200" });
        if (body.ContextLines is < 0 or > 50)
            return BadRequest(new { error = "contextLines must be between 0 and 50" });

        var prompt = string.IsNullOrWhiteSpace(body.PromptTemplate) ? null : body.PromptTemplate.Replace("\r\n", "\n");
        if (prompt != null && !prompt.Contains("{batch}", StringComparison.Ordinal))
            return BadRequest(new { error = "promptTemplate must contain the {batch} placeholder" });

        if (prompt != null)
        {
            try
            {
                var defaults = await _refiner.GetDefaultsAsync(ct);
                if (string.Equals(prompt.Trim(), defaults.UserPromptTemplate.Replace("\r\n", "\n").Trim(), StringComparison.Ordinal))
                    prompt = null; // identical to built-in → don't pin a copy that would hide future default improvements
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Agent06 defaults unavailable while saving; keeping prompt as custom");
            }
        }

        var model = string.IsNullOrWhiteSpace(body.Model) ? null : body.Model.Trim();
        _settings.Save(new RefinerSettings(prompt, body.BatchSize, body.ContextLines, model));
        return await GetSettings(ct);
    }

    /// <summary>
    /// Batches of the job's last refiner run, as saved by agent06 under <c>refine_batches/</c>:
    /// order, line range, context (the seam with the previous batch), text sent and text received.
    /// </summary>
    [HttpGet("api/jobs/{id}/refine-batches")]
    public async Task<IActionResult> GetRefineBatches(string id, CancellationToken ct = default)
    {
        var dir = Path.Combine(_workspace.GetJobDirectoryPath(id), TranscriptionRefinerPipeline.RefineBatchesDir);
        if (!Directory.Exists(dir))
            return Ok(new { promptPreview = (string?)null, batches = Array.Empty<object>() });

        string? prompt = null;
        var promptPath = Path.Combine(dir, "prompt.txt");
        if (System.IO.File.Exists(promptPath))
            prompt = await System.IO.File.ReadAllTextAsync(promptPath, Encoding.UTF8, ct);

        var batches = new List<JsonElement>();
        foreach (var f in Directory.EnumerateFiles(dir, "batch_*.json").Order(StringComparer.Ordinal))
        {
            try
            {
                using var doc = JsonDocument.Parse(await System.IO.File.ReadAllTextAsync(f, Encoding.UTF8, ct));
                batches.Add(doc.RootElement.Clone());
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Job {JobId}: skipping unreadable refine batch file {File}", id, Path.GetFileName(f));
            }
        }

        return Ok(new { promptPreview = prompt, batches });
    }
}

public sealed class RefinerSettingsRequest
{
    public string? PromptTemplate { get; set; }
    public int? BatchSize { get; set; }
    public int? ContextLines { get; set; }
    public string? Model { get; set; }
}
