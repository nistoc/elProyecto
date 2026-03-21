using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using XtractManager.Features.Jobs.Application;
using XtractManager.Infrastructure;

namespace XtractManager.Features.Jobs.Infrastructure;

/// <summary>
/// Fills <see cref="JobSnapshot.Chunks"/> and optional phase/status from on-disk artifacts
/// (same layout as Agent04: <c>transcription_work_state.json</c> under the artifact root).
/// </summary>
public static class JobSnapshotDiskEnricher
{
    public const string TranscriptionWorkStateFileName = "transcription_work_state.json";
    public const string XtractUiStateFileName = "xtract_ui_state.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// Merges disk data into <paramref name="snap"/> when chunks are missing or empty.
    /// Applies <see cref="XtractUiState"/> phase/status when the snapshot looks like an archive (idle/completed).
    /// </summary>
    public static void TryEnrichFromDisk(JobSnapshot snap, string jobDirectoryPath, ILogger logger)
    {
        if (string.IsNullOrWhiteSpace(jobDirectoryPath) || !Directory.Exists(jobDirectoryPath))
            return;

        try
        {
            TryApplyUiStateFile(snap, jobDirectoryPath, logger);

            var needsChunks =
                snap.Chunks == null
                || snap.Chunks.Total <= 0
                || snap.Chunks.ChunkVirtualModel is not { Count: > 0 };

            if (!needsChunks)
                return;

            var artifactRoot = ResolveArtifactRoot(jobDirectoryPath);
            var statePath = Path.Combine(artifactRoot, TranscriptionWorkStateFileName);
            TranscriptionWorkStateDocument? doc = null;
            if (File.Exists(statePath))
            {
                try
                {
                    var json = File.ReadAllText(statePath);
                    doc = JsonSerializer.Deserialize<TranscriptionWorkStateDocument>(json, JsonOptions);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Could not read {File} for job {JobId}", statePath, snap.Id);
                }
            }

            if (doc != null && (doc.TotalChunks > 0 || doc.Chunks is { Count: > 0 }))
            {
                ApplyWorkStateDocument(snap, doc);
                logger.LogDebug("Disk enrich: loaded chunk VM from {Path} for job {JobId}", statePath, snap.Id);
                return;
            }

            var heuristic = TryBuildChunksFromChunkFolders(artifactRoot);
            if (heuristic != null)
            {
                snap.Chunks = heuristic;
                logger.LogDebug("Disk enrich: heuristic chunks for job {JobId} under {Root}", snap.Id, artifactRoot);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Disk enrichment failed for job {JobId} at {Path}", snap.Id, jobDirectoryPath);
        }
    }

    /// <summary>Writes UI-facing phase/status for archive hydration (best-effort).</summary>
    public static async Task TryWriteUiStateAsync(string jobDirectoryPath, JobSnapshot snap, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(jobDirectoryPath) || !Directory.Exists(jobDirectoryPath))
            return;
        try
        {
            var path = Path.Combine(jobDirectoryPath, XtractUiStateFileName);
            var dto = new XtractUiState
            {
                Phase = snap.Phase,
                Status = snap.Status,
                Agent04JobId = snap.Agent04JobId,
                UpdatedAt = DateTime.UtcNow.ToString("O")
            };
            var tmp = path + ".tmp";
            var json = JsonSerializer.Serialize(dto, ApiJson.CamelCase);
            await File.WriteAllTextAsync(tmp, json, ct).ConfigureAwait(false);
            File.Move(tmp, path, overwrite: true);
        }
        catch
        {
            /* best-effort */
        }
    }

    private static void TryApplyUiStateFile(JobSnapshot snap, string jobDirectoryPath, ILogger logger)
    {
        var path = Path.Combine(jobDirectoryPath, XtractUiStateFileName);
        if (!File.Exists(path)) return;
        try
        {
            var json = File.ReadAllText(path);
            var ui = JsonSerializer.Deserialize<XtractUiState>(json, JsonOptions);
            if (ui == null) return;
            // Only override for disk-only archive rows — never clobber an in-memory live job.
            var looksLikeArchive = string.Equals(snap.Phase, "idle", StringComparison.OrdinalIgnoreCase)
                && string.Equals(snap.Status, "completed", StringComparison.OrdinalIgnoreCase);
            if (!looksLikeArchive) return;
            if (!string.IsNullOrEmpty(ui.Phase))
                snap.Phase = ui.Phase;
            if (!string.IsNullOrEmpty(ui.Status))
                snap.Status = ui.Status;
            if (!string.IsNullOrEmpty(ui.Agent04JobId))
                snap.Agent04JobId = ui.Agent04JobId;
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Could not read {File} for job {JobId}", path, snap.Id);
        }
    }

    /// <summary>Artifact root = job directory when audio lives at job root (XtractManager upload).</summary>
    private static string ResolveArtifactRoot(string jobDirectoryPath) =>
        Path.GetFullPath(jobDirectoryPath);

    private static void ApplyWorkStateDocument(JobSnapshot snap, TranscriptionWorkStateDocument doc)
    {
        snap.Chunks ??= new ChunkState();
        if (doc.TotalChunks > 0)
            snap.Chunks.Total = doc.TotalChunks;
        var rows = doc.Chunks;
        if (rows is not { Count: > 0 }) return;
        if (snap.Chunks.Total <= 0)
            snap.Chunks.Total = rows.Max(c => c.Index) + 1;
        var vm = new List<ChunkVirtualModelEntry>(rows.Count);
        foreach (var c in rows.OrderBy(x => x.Index))
        {
            vm.Add(new ChunkVirtualModelEntry
            {
                Index = c.Index,
                State = string.IsNullOrEmpty(c.State) ? "Pending" : c.State,
                StartedAt = c.StartedAt,
                CompletedAt = c.CompletedAt,
                ErrorMessage = c.ErrorMessage
            });
        }
        snap.Chunks.ChunkVirtualModel = vm;
    }

    private static ChunkState? TryBuildChunksFromChunkFolders(string artifactRoot)
    {
        var chunksDir = Path.Combine(artifactRoot, "chunks");
        if (!Directory.Exists(chunksDir)) return null;
        var exts = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".wav", ".m4a", ".mp3", ".flac", ".ogg" };
        var files = Directory.EnumerateFiles(chunksDir)
            .Where(f => exts.Contains(Path.GetExtension(f)))
            .Select(f =>
            {
                var name = Path.GetFileNameWithoutExtension(f);
                return int.TryParse(name, out var idx) ? idx : (int?)null;
            })
            .Where(i => i.HasValue)
            .Select(i => i!.Value)
            .Distinct()
            .OrderBy(i => i)
            .ToList();
        if (files.Count == 0) return null;
        var total = files.Max() + 1;
        var vm = new List<ChunkVirtualModelEntry>();
        for (var i = 0; i < total; i++)
        {
            vm.Add(new ChunkVirtualModelEntry
            {
                Index = i,
                State = files.Contains(i) ? "Pending" : "Pending"
            });
        }
        return new ChunkState
        {
            Total = total,
            ChunkVirtualModel = vm
        };
    }

    private sealed class TranscriptionWorkStateDocument
    {
        public int SchemaVersion { get; set; }
        public int TotalChunks { get; set; }
        public bool RecoveredFromArtifacts { get; set; }
        public List<TranscriptionWorkStateChunkRow>? Chunks { get; set; }
    }

    private sealed class TranscriptionWorkStateChunkRow
    {
        public int Index { get; set; }
        public string State { get; set; } = "";
        public string? StartedAt { get; set; }
        public string? CompletedAt { get; set; }
        public string? ErrorMessage { get; set; }
    }

    private sealed class XtractUiState
    {
        public string? Phase { get; set; }
        public string? Status { get; set; }
        public string? Agent04JobId { get; set; }
        public string? UpdatedAt { get; set; }
    }
}
