using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
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
            TryEnrichRefinerCheckpointProgress(snap, jobDirectoryPath);

            var needsChunks =
                snap.Chunks == null
                || snap.Chunks.Total <= 0
                || snap.Chunks.ChunkVirtualModel is not { Count: > 0 };

            if (!needsChunks)
            {
                TryMergeSubChunkVirtualModelFromDisk(snap, jobDirectoryPath, logger);
            }
            else
            {
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
                }
                else
                {
                    var heuristic = TryBuildChunksFromChunkFolders(artifactRoot);
                    if (heuristic != null)
                    {
                        snap.Chunks = heuristic;
                        logger.LogDebug("Disk enrich: heuristic chunks for job {JobId} under {Root}", snap.Id, artifactRoot);
                    }
                }
            }

            TryHydrateRefinerThreadBatchesFromDisk(snap, jobDirectoryPath, logger);
            TryInferRefinerPhaseFromArtifacts(snap, jobDirectoryPath, logger);
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
                Agent06RefineJobId = snap.Agent06RefineJobId,
                MdOutputPath = snap.MdOutputPath,
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

    private static void TryEnrichRefinerCheckpointProgress(JobSnapshot snap, string jobDirectoryPath)
    {
        if (!RefinerCheckpointProgressReader.TryRead(jobDirectoryPath, out var sum) || sum == null || !sum.CanResume)
        {
            snap.RefinerCheckpointNextBatchIndex0 = null;
            snap.RefinerCheckpointTotalBatches = null;
            snap.RefinerCheckpointRemainingBatches = null;
            return;
        }
        snap.RefinerCheckpointNextBatchIndex0 = sum.NextBatchIndex0;
        snap.RefinerCheckpointTotalBatches = sum.TotalBatches;
        snap.RefinerCheckpointRemainingBatches = sum.RemainingBatches;
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
            if (!string.IsNullOrEmpty(ui.Agent06RefineJobId))
                snap.Agent06RefineJobId = ui.Agent06RefineJobId;
            if (!string.IsNullOrEmpty(ui.MdOutputPath))
                snap.MdOutputPath = ui.MdOutputPath;
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Could not read {File} for job {JobId}", path, snap.Id);
        }
    }

    /// <summary>Artifact root = job directory when audio lives at job root (XtractManager upload).</summary>
    private static string ResolveArtifactRoot(string jobDirectoryPath) =>
        Path.GetFullPath(jobDirectoryPath);

    /// <summary>
    /// Live jobs often have a non-empty <see cref="ChunkState.ChunkVirtualModel"/> from SSE without sub-chunk rows.
    /// Work state on disk is authoritative for sub-chunk progress; merge those rows without replacing main-chunk VM.
    /// </summary>
    private static void TryMergeSubChunkVirtualModelFromDisk(JobSnapshot snap, string jobDirectoryPath, ILogger logger)
    {
        try
        {
            var artifactRoot = ResolveArtifactRoot(jobDirectoryPath);
            var statePath = Path.Combine(artifactRoot, TranscriptionWorkStateFileName);
            if (!File.Exists(statePath))
                return;

            string json;
            try
            {
                json = File.ReadAllText(statePath);
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Could not read {File} for sub-chunk VM merge (job {JobId})", statePath, snap.Id);
                return;
            }

            var doc = JsonSerializer.Deserialize<TranscriptionWorkStateDocument>(json, JsonOptions);
            if (doc?.Chunks is not { Count: > 0 })
                return;

            var subRows = doc.Chunks.Where(c => c.IsSubChunk).ToList();
            if (subRows.Count == 0)
                return;

            snap.Chunks ??= new ChunkState();
            var vm = snap.Chunks.ChunkVirtualModel?.ToList() ?? new List<ChunkVirtualModelEntry>();

            foreach (var c in subRows)
            {
                var existing = vm.FirstOrDefault(e =>
                    e.IsSubChunk
                    && e.ParentChunkIndex == c.ParentChunkIndex
                    && e.SubChunkIndex == c.SubChunkIndex);
                if (existing != null)
                {
                    existing.Index = c.Index;
                    existing.State = string.IsNullOrEmpty(c.State) ? "Pending" : c.State;
                    existing.StartedAt = c.StartedAt;
                    existing.CompletedAt = c.CompletedAt;
                    existing.ErrorMessage = c.ErrorMessage;
                }
                else
                {
                    vm.Add(new ChunkVirtualModelEntry
                    {
                        Index = c.Index,
                        State = string.IsNullOrEmpty(c.State) ? "Pending" : c.State,
                        StartedAt = c.StartedAt,
                        CompletedAt = c.CompletedAt,
                        ErrorMessage = c.ErrorMessage,
                        IsSubChunk = true,
                        ParentChunkIndex = c.ParentChunkIndex,
                        SubChunkIndex = c.SubChunkIndex
                    });
                }
            }

            snap.Chunks.ChunkVirtualModel = vm;
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Sub-chunk VM merge from disk failed for job {JobId}", snap.Id);
        }
    }

    private static void ApplyWorkStateDocument(JobSnapshot snap, TranscriptionWorkStateDocument doc)
    {
        snap.Chunks ??= new ChunkState();
        if (doc.TotalChunks > 0)
            snap.Chunks.Total = doc.TotalChunks;
        var rows = doc.Chunks;
        if (rows is not { Count: > 0 }) return;
        if (snap.Chunks.Total <= 0)
        {
            var mains = rows.Where(r => !r.IsSubChunk).ToList();
            if (mains.Count > 0)
                snap.Chunks.Total = mains.Max(c => c.Index) + 1;
            else
            {
                var subs = rows.Where(r => r.IsSubChunk).ToList();
                snap.Chunks.Total = subs.Count > 0 ? subs.Max(c => c.ParentChunkIndex) + 1 : rows.Max(c => c.Index) + 1;
            }
        }

        var vm = new List<ChunkVirtualModelEntry>(rows.Count);
        foreach (var c in rows
                     .OrderBy(x => x.Index)
                     .ThenBy(x => x.IsSubChunk)
                     .ThenBy(x => x.SubChunkIndex))
        {
            vm.Add(new ChunkVirtualModelEntry
            {
                Index = c.Index,
                State = string.IsNullOrEmpty(c.State) ? "Pending" : c.State,
                StartedAt = c.StartedAt,
                CompletedAt = c.CompletedAt,
                ErrorMessage = c.ErrorMessage,
                IsSubChunk = c.IsSubChunk,
                ParentChunkIndex = c.ParentChunkIndex,
                SubChunkIndex = c.SubChunkIndex
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

    /// <summary>
    /// True when the job directory has a markdown that can be used as refiner input (excludes refiner outputs).
    /// Matches UI fallback: any job-root .md except <c>transcript_fixed*</c>.
    /// </summary>
    public static bool JobDirectoryHasTranscriptMarkdown(string jobDirectoryPath)
    {
        if (string.IsNullOrWhiteSpace(jobDirectoryPath) || !Directory.Exists(jobDirectoryPath))
            return false;
        foreach (var f in Directory.EnumerateFiles(jobDirectoryPath, "*.md", SearchOption.TopDirectoryOnly))
        {
            var n = Path.GetFileName(f);
            if (IsRefinerOutputMarkdown(n)) continue;
            return true;
        }
        return false;
    }

    private static bool IsRefinerOutputMarkdown(string fileName)
    {
        if (fileName.Equals("transcript_fixed.md", StringComparison.OrdinalIgnoreCase)) return true;
        return Regex.IsMatch(fileName, @"^transcript_fixed_\d+\.md$", RegexOptions.IgnoreCase);
    }

    private static bool JobRootHasTranscriptFixed(string jobDirectoryPath)
    {
        if (!Directory.Exists(jobDirectoryPath)) return false;
        if (File.Exists(Path.Combine(jobDirectoryPath, "transcript_fixed.md"))) return true;
        return Directory.GetFiles(jobDirectoryPath, "transcript_fixed_*.md").Length > 0;
    }

    private static void TryHydrateRefinerThreadBatchesFromDisk(
        JobSnapshot snap,
        string jobDirectoryPath,
        ILogger logger)
    {
        if (snap.RefinerThreadBatches is { Count: > 0 })
            return;

        // Live run: StartRefiner clears batches; every GetAsync (snapshot broadcast) hits this with Count==0.
        // On-disk batch_*.json often still has afterText=null until Agent06 finishes that batch — loading here
        // replaces the in-memory list and races the gRPC stream so early batches never get AFTER in the UI.
        // Paused / archive: still hydrate when empty so refresh shows checkpoint progress from disk.
        if (string.Equals(snap.Phase, "refiner", StringComparison.OrdinalIgnoreCase))
            return;

        var threadsDir = Path.Combine(jobDirectoryPath, "refiner_threads");
        if (!Directory.Exists(threadsDir))
            return;
        var files = Directory.GetFiles(threadsDir, "batch_*.json").OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();
        if (files.Count == 0)
            return;
        var list = new List<RefinerThreadBatchEntry>();
        foreach (var path in files)
        {
            try
            {
                var json = File.ReadAllText(path);
                var row = JsonSerializer.Deserialize<RefinerThreadBatchEntry>(json, JsonOptions);
                if (row == null) continue;
                list.Add(row);
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Skip invalid refiner thread batch {Path} for job {JobId}", path, snap.Id);
            }
        }
        if (list.Count == 0)
            return;
        list.Sort((a, b) => a.BatchIndex.CompareTo(b.BatchIndex));
        snap.RefinerThreadBatches = list;
        logger.LogDebug("Disk enrich: loaded {Count} refiner thread batch(es) for job {JobId}", list.Count, snap.Id);
    }

    private static void TryInferRefinerPhaseFromArtifacts(
        JobSnapshot snap,
        string jobDirectoryPath,
        ILogger logger)
    {
        if (!string.Equals(snap.Phase, "idle", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(snap.Status, "completed", StringComparison.OrdinalIgnoreCase))
            return;

        if (JobRootHasTranscriptFixed(jobDirectoryPath))
        {
            snap.Phase = "completed";
            snap.Status = "done";
            logger.LogDebug("Disk enrich: inferred phase completed from transcript_fixed for job {JobId}", snap.Id);
        }
        else if (snap.RefinerThreadBatches is { Count: > 0 })
        {
            var allAfter = snap.RefinerThreadBatches.All(b => b.AfterText != null);
            if (allAfter)
            {
                snap.Phase = "completed";
                snap.Status = "done";
                logger.LogDebug("Disk enrich: inferred completed from refiner batches for job {JobId}", snap.Id);
            }
            else
            {
                snap.Phase = "refiner_paused";
                snap.Status = "running";
                logger.LogDebug("Disk enrich: inferred refiner_paused from partial batches for job {JobId}", snap.Id);
            }
        }
        else if (JobDirectoryHasTranscriptMarkdown(jobDirectoryPath))
        {
            snap.Phase = "awaiting_refiner";
            snap.Status = "done";
            logger.LogDebug("Disk enrich: inferred awaiting_refiner for job {JobId}", snap.Id);
        }
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
        public bool IsSubChunk { get; set; }
        public int ParentChunkIndex { get; set; }
        public int SubChunkIndex { get; set; }
    }

    private sealed class XtractUiState
    {
        public string? Phase { get; set; }
        public string? Status { get; set; }
        public string? Agent04JobId { get; set; }
        /// <summary>Agent06 gRPC refine job id — needed to Resume after API restart.</summary>
        public string? Agent06RefineJobId { get; set; }
        /// <summary>Agent04 transcript path (relative) — restores stem selection for Refine row after refresh.</summary>
        public string? MdOutputPath { get; set; }
        public string? UpdatedAt { get; set; }
    }
}
