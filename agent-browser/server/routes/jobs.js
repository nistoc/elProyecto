import express from "express";
import multer from "multer";
import fs from "fs";
import path from "path";
import crypto from "crypto";
import { execSync } from "child_process";
import { RUNTIME_DIR, UPLOADS_DIR, AGENT_ALIASES } from "../config.js";
import { createJob, getJob, getAllJobs, deleteJob } from "../services/jobStore.js";
import {
  broadcast,
  subscribe,
  pushLog,
  updateJobAndBroadcast,
} from "../services/broadcaster.js";
import { runPipeline, runRefinerStage, rebuildTranscript } from "../services/pipeline.js";
import {
  startSplitJob,
  cancelSubChunk,
  skipChunk,
  retranscribeSubChunk,
  ensureSplitJobsField,
  getChunkAudioPath,
  getAllChunkPaths,
} from "../services/splitService.js";

const router = express.Router();
const upload = multer({ dest: UPLOADS_DIR });

/**
 * POST /api/jobs - Create a new job
 */
router.post("/", upload.single("file"), async (req, res) => {
  if (!req.file) {
    return res.status(400).json({ error: "file is required" });
  }

  const jobId = crypto.randomUUID();
  const jobDir = path.join(RUNTIME_DIR, jobId);
  fs.mkdirSync(jobDir, { recursive: true });

  const savedAudioPath = path.join(jobDir, req.file.originalname);
  fs.renameSync(req.file.path, savedAudioPath);

  const job = createJob({
    id: jobId,
    status: "queued",
    phase: "idle",
    logs: [],
    dir: jobDir,
    cancelDir: path.join(jobDir, "cancel_signals"),
    chunks: {
      total: 0,
      active: [],
      completed: [],
      cancelled: [],
      failed: [],
    },
    audio: savedAudioPath,
    result: {},
  });

  fs.mkdirSync(job.cancelDir, { recursive: true });

  res.json({ jobId });

  // Run pipeline asynchronously
  try {
    await runPipeline(job, jobDir);
  } catch (err) {
    pushLog(jobId, `Pipeline failed: ${err?.message || err}`, "error");
    updateJobAndBroadcast(jobId, { status: "failed" });
  }
});

/**
 * GET /api/jobs - Get list of all jobs with metadata
 */
router.get("/", (req, res) => {
  try {
    const allJobs = getAllJobs();
    const jobsMap = new Map();

    // Process jobs from in-memory store
    allJobs.forEach((job) => {
      jobsMap.set(job.id, job);
    });

    // Also scan runtime directory for jobs that might not be in memory
    // (e.g., after server restart)
    try {
      const runtimeDirs = fs.readdirSync(RUNTIME_DIR, { withFileTypes: true });
      for (const dirent of runtimeDirs) {
        if (dirent.isDirectory()) {
          const jobId = dirent.name;
          // Skip special directories like "uploads"
          if (jobId === "uploads") continue;
          
          // Check if it's a valid UUID format
          const uuidRegex = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
          if (!uuidRegex.test(jobId)) continue;

          // If job is not in memory, create a minimal job object from directory
          if (!jobsMap.has(jobId)) {
            const jobDir = path.join(RUNTIME_DIR, jobId);
            jobsMap.set(jobId, {
              id: jobId,
              dir: jobDir,
              status: "done", // Assume done if not in memory
              phase: "completed",
              audio: null,
            });
          }
        }
      }
    } catch (err) {
      // Runtime directory doesn't exist or can't be read - continue with in-memory jobs only
    }

    const jobsList = Array.from(jobsMap.values()).map((job) => {
      // Extract original filename from audio path (optimized - only get first audio file)
      let originalFilename = "Unknown";
      if (job.audio) {
        originalFilename = path.basename(job.audio);
      } else {
        // Try to find first audio file in job directory (optimized - stop at first match)
        try {
          const files = fs.readdirSync(job.dir, { withFileTypes: true });
          for (const dirent of files) {
            if (dirent.isFile()) {
              const fileName = dirent.name;
              if (/\.(m4a|mp3|wav|ogg|flac)$/i.test(fileName)) {
                originalFilename = fileName;
                break; // Stop at first audio file found
              }
            }
          }
        } catch (err) {
          // Directory doesn't exist or can't read
        }
      }

      // Get directory creation time as job creation time
      let createdAt = null;
      try {
        const stats = fs.statSync(job.dir);
        createdAt = stats.birthtime || stats.mtime;
      } catch (err) {
        // Directory doesn't exist
      }

      // Get completion time for completed jobs (use transcript file modification time)
      let completedAt = null;
      if (job.status === "done") {
        try {
          // Check for fixed transcript first, then regular transcript
          const transcriptFixedPath = path.join(job.dir, "transcript_fixed.md");
          const transcriptPath = path.join(job.dir, "transcript.md");
          
          if (fs.existsSync(transcriptFixedPath)) {
            const stats = fs.statSync(transcriptFixedPath);
            completedAt = stats.mtime;
          } else if (fs.existsSync(transcriptPath)) {
            const stats = fs.statSync(transcriptPath);
            completedAt = stats.mtime;
          }
        } catch (err) {
          // Can't read transcript file
        }
      }

      return {
        id: job.id,
        originalFilename,
        status: job.status,
        phase: job.phase,
        createdAt: createdAt ? createdAt.toISOString() : null,
        completedAt: completedAt ? completedAt.toISOString() : null,
      };
    });

    // Sort by creation time, newest first
    jobsList.sort((a, b) => {
      if (!a.createdAt) return 1;
      if (!b.createdAt) return -1;
      return new Date(b.createdAt) - new Date(a.createdAt);
    });

    res.json({ jobs: jobsList });
  } catch (err) {
    console.error("[GET /api/jobs] Error:", err);
    res.status(500).json({ error: err?.message || "Failed to get jobs list" });
  }
});

/**
 * GET /api/jobs/:id - Get job status
 */
router.get("/:id", (req, res) => {
  // Validate that id is a valid UUID format
  const uuidRegex = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
  if (!uuidRegex.test(req.params.id)) {
    return res.status(400).json({ error: "invalid job id format" });
  }

  let job = getJob(req.params.id);
  
  // If job not in memory, try to restore it from filesystem
  if (!job) {
    const jobDir = path.join(RUNTIME_DIR, req.params.id);
    if (fs.existsSync(jobDir)) {
      // Restore job from filesystem
      // Find original audio file
      let audioPath = null;
      try {
        const files = fs.readdirSync(jobDir);
        const audioFiles = files.filter((f) =>
          /\.(m4a|mp3|wav|ogg|flac)$/i.test(f)
        );
        if (audioFiles.length > 0) {
          audioPath = path.join(jobDir, audioFiles[0]);
        }
      } catch (err) {
        // Can't read directory
      }

      // Check if transcript exists to determine status
      const transcriptPath = path.join(jobDir, "transcript.md");
      const transcriptFixedPath = path.join(jobDir, "transcript_fixed.md");
      const hasTranscript = fs.existsSync(transcriptPath);
      const hasFixedTranscript = fs.existsSync(transcriptFixedPath);
      
      // Find all transcript_fixed files
      const allFixedFiles = [];
      try {
        const files = fs.readdirSync(jobDir);
        allFixedFiles.push(...files
          .filter(f => f.startsWith("transcript_fixed") && f.endsWith(".md"))
          .map(f => `runtime/${req.params.id}/${f}`)
          .sort()
        );
      } catch (err) {
        // Can't read directory
      }

      // Determine status and phase based on files
      let status = "done";
      let phase = "completed";
      if (!hasTranscript && allFixedFiles.length === 0) {
        status = "failed";
        phase = "idle";
      } else if (allFixedFiles.length > 0 || hasFixedTranscript) {
        status = "done";
        phase = "completed";
      } else if (hasTranscript) {
        status = "done";
        phase = "completed";
      }

      // Count chunks and restore cancelled/failed state
      const chunksDir = path.join(jobDir, "chunks");
      const cancelDir = path.join(jobDir, "cancel_signals");
      let totalChunks = 0;
      let completedChunks = [];
      let cancelledChunks = [];
      let failedChunks = [];
      
      try {
        if (fs.existsSync(chunksDir)) {
          const chunkFiles = fs.readdirSync(chunksDir);
          totalChunks = chunkFiles.filter((f) =>
            /\.(m4a|mp3|wav|ogg|flac)$/i.test(f)
          ).length;
          
          // Restore cancelled chunks from cancel_signals directory
          if (fs.existsSync(cancelDir)) {
            try {
              const cancelFiles = fs.readdirSync(cancelDir);
              for (const file of cancelFiles) {
                const match = file.match(/^cancel_(\d+)\.flag$/);
                if (match) {
                  const idx = parseInt(match[1], 10);
                  if (!isNaN(idx) && idx >= 0 && idx < totalChunks) {
                    cancelledChunks.push(idx);
                  }
                }
              }
            } catch (err) {
              // Can't read cancel directory
            }
          }
          
          // Determine completed chunks (all chunks that are not cancelled)
          // We can't determine failed chunks from filesystem alone, so we'll mark
          // non-cancelled chunks as completed
          for (let i = 0; i < totalChunks; i++) {
            if (!cancelledChunks.includes(i)) {
              completedChunks.push(i);
            }
          }
        }
      } catch (err) {
        // Can't read chunks directory
      }

      // Create minimal job object
      job = createJob({
        id: req.params.id,
        status: status,
        phase: phase,
        logs: [],
        dir: jobDir,
        cancelDir: path.join(jobDir, "cancel_signals"),
        chunks: {
          total: totalChunks,
          active: [],
          completed: completedChunks,
          cancelled: cancelledChunks,
          failed: failedChunks,
        },
        audio: audioPath,
        result: {
          transcript: hasTranscript ? `runtime/${req.params.id}/transcript.md` : undefined,
          transcriptFixed: hasFixedTranscript
            ? `runtime/${req.params.id}/transcript_fixed.md`
            : undefined,
          transcriptFixedAll: allFixedFiles.length > 0 ? allFixedFiles : undefined,
          rawJson: fs.existsSync(path.join(jobDir, "response.json"))
            ? `runtime/${req.params.id}/response.json`
            : undefined,
        },
      });
    }
  }

  if (!job) {
    return res.status(404).json({ error: "job not found" });
  }

  // Ensure splitJobs is synced to chunks
  ensureSplitJobsField(job);

  res.json({
    id: job.id,
    status: job.status,
    phase: job.phase,
    logs: job.logs,
    chunks: job.chunks,
    result: job.result,
  });
});

/**
 * POST /api/jobs/:id/chunks/:idx/cancel - Cancel a chunk
 */
router.post("/:id/chunks/:idx/cancel", (req, res) => {
  const job = getJob(req.params.id);
  if (!job) {
    return res.status(404).json({ error: "job not found" });
  }

  const idx = Number.parseInt(req.params.idx, 10);
  if (Number.isNaN(idx) || idx < 0) {
    return res.status(400).json({ error: "invalid chunk index" });
  }

  try {
    fs.mkdirSync(job.cancelDir, { recursive: true });
    const flagPath = path.join(job.cancelDir, `cancel_${idx}.flag`);
    fs.writeFileSync(flagPath, "cancelled");
    pushLog(
      job.id,
      `[${AGENT_ALIASES.transcriber}] requested cancel for chunk #${idx + 1}`
    );
    broadcast(job.id, { type: "chunk", payload: { status: "cancelled", idx } });
    return res.json({ ok: true });
  } catch (err) {
    return res.status(500).json({ error: err?.message || "failed to cancel" });
  }
});

/**
 * POST /api/jobs/:id/chunks/:idx/split - Split a failed/cancelled chunk into parts
 * Body: { parts: 2 | 3 | 4 }
 */
router.post("/:id/chunks/:idx/split", async (req, res) => {
  const job = getJob(req.params.id);
  if (!job) {
    return res.status(404).json({ error: "job not found" });
  }

  const idx = Number.parseInt(req.params.idx, 10);
  if (Number.isNaN(idx) || idx < 0) {
    return res.status(400).json({ error: "invalid chunk index" });
  }

  const parts = Number.parseInt(req.body.parts, 10) || 2;
  if (parts < 2 || parts > 4) {
    return res.status(400).json({ error: "parts must be 2, 3, or 4" });
  }

  // Check that this chunk is in failed or cancelled state
  const isFailed = job.chunks?.failed?.includes(idx);
  const isCancelled = job.chunks?.cancelled?.includes(idx);
  if (!isFailed && !isCancelled) {
    return res.status(400).json({ 
      error: "chunk must be in failed or cancelled state to split" 
    });
  }

  try {
    // Start split job asynchronously
    res.json({ ok: true, message: `Starting split of chunk #${idx + 1} into ${parts} parts` });
    
    // Run in background
    startSplitJob(job, idx, parts).catch((err) => {
      pushLog(job.id, `Split job failed: ${err.message}`, "error");
    });
  } catch (err) {
    return res.status(500).json({ error: err?.message || "failed to start split" });
  }
});

/**
 * POST /api/jobs/:id/chunks/:idx/sub/:subIdx/cancel - Cancel a sub-chunk
 */
router.post("/:id/chunks/:idx/sub/:subIdx/cancel", (req, res) => {
  const job = getJob(req.params.id);
  if (!job) {
    return res.status(404).json({ error: "job not found" });
  }

  const idx = Number.parseInt(req.params.idx, 10);
  const subIdx = Number.parseInt(req.params.subIdx, 10);
  
  if (Number.isNaN(idx) || idx < 0 || Number.isNaN(subIdx) || subIdx < 0) {
    return res.status(400).json({ error: "invalid chunk or sub-chunk index" });
  }

  try {
    cancelSubChunk(job, idx, subIdx);
    return res.json({ ok: true });
  } catch (err) {
    return res.status(500).json({ error: err?.message || "failed to cancel sub-chunk" });
  }
});

/**
 * POST /api/jobs/:id/chunks/:idx/skip - Permanently skip a chunk
 */
router.post("/:id/chunks/:idx/skip", (req, res) => {
  const job = getJob(req.params.id);
  if (!job) {
    return res.status(404).json({ error: "job not found" });
  }

  const idx = Number.parseInt(req.params.idx, 10);
  if (Number.isNaN(idx) || idx < 0) {
    return res.status(400).json({ error: "invalid chunk index" });
  }

  try {
    skipChunk(job, idx);
    return res.json({ ok: true });
  } catch (err) {
    return res.status(500).json({ error: err?.message || "failed to skip chunk" });
  }
});

/**
 * POST /api/jobs/:id/chunks/:idx/sub/:subIdx/retranscribe - Retranscribe a specific sub-chunk
 */
router.post("/:id/chunks/:idx/sub/:subIdx/retranscribe", async (req, res) => {
  const job = getJob(req.params.id);
  if (!job) {
    return res.status(404).json({ error: "job not found" });
  }

  const idx = Number.parseInt(req.params.idx, 10);
  const subIdx = Number.parseInt(req.params.subIdx, 10);
  
  if (Number.isNaN(idx) || idx < 0 || Number.isNaN(subIdx) || subIdx < 0) {
    return res.status(400).json({ error: "invalid chunk or sub-chunk index" });
  }

  try {
    res.json({ ok: true, message: "Starting retranscription..." });
    
    // Run retranscription in background
    retranscribeSubChunk(job, idx, subIdx).catch((err) => {
      pushLog(job.id, `Retranscription failed: ${err.message}`, "error");
    });
  } catch (err) {
    return res.status(500).json({ error: err?.message || "failed to retranscribe sub-chunk" });
  }
});

/**
 * POST /api/jobs/:id/rebuild-transcript - Rebuild transcript.md from chunk transcripts
 * Useful after editing individual chunk transcript files
 */
router.post("/:id/rebuild-transcript", async (req, res) => {
  const job = getJob(req.params.id);
  if (!job) {
    return res.status(404).json({ error: "job not found" });
  }

  const jobDir = job.dir;
  if (!jobDir || !fs.existsSync(jobDir)) {
    return res.status(400).json({ 
      error: "Job directory not found" 
    });
  }

  try {
    res.json({ ok: true, message: "Rebuilding transcript..." });
    
    // Run rebuild in background
    rebuildTranscript(job).catch((err) => {
      pushLog(job.id, `Failed to rebuild transcript: ${err.message}`, "error");
    });
  } catch (err) {
    return res.status(500).json({ error: err?.message || "failed to rebuild transcript" });
  }
});

/**
 * POST /api/jobs/:id/start-refiner - Manually start the refiner stage
 * Can be called for any project that has a transcript.md file
 */
router.post("/:id/start-refiner", async (req, res) => {
  const job = getJob(req.params.id);
  if (!job) {
    return res.status(404).json({ error: "job not found" });
  }

  const transcriptPath = job.transcriptPath || path.join(job.dir, "transcript.md");
  if (!fs.existsSync(transcriptPath)) {
    return res.status(400).json({ 
      error: "Transcript file not found. Complete transcription first." 
    });
  }

  try {
    res.json({ ok: true, message: "Starting refiner..." });
    
    // Run refiner in background
    runRefinerStage(job).catch((err) => {
      pushLog(job.id, `Refiner failed: ${err.message}`, "error");
      updateJobAndBroadcast(job.id, { status: "failed" });
    });
  } catch (err) {
    return res.status(500).json({ error: err?.message || "failed to start refiner" });
  }
});

/**
 * POST /api/jobs/:id/pause-agent - Pause agent (transcriber or refiner)
 */
router.post("/:id/pause-agent", (req, res) => {
  const { id } = req.params;
  const { agent } = req.body; // "transcriber" or "refiner"
  
  // Validate UUID format
  const uuidRegex = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
  if (!uuidRegex.test(id)) {
    return res.status(400).json({ error: "invalid job id format" });
  }

  if (!agent || (agent !== "transcriber" && agent !== "refiner")) {
    return res.status(400).json({ error: "agent must be 'transcriber' or 'refiner'" });
  }

  const job = getJob(id);
  if (!job) {
    return res.status(404).json({ error: "job not found" });
  }

  try {
    const pauseFlagPath = path.join(job.dir, "pause_agent.flag");
    fs.writeFileSync(pauseFlagPath, agent, 'utf8');
    
    updateJobAndBroadcast(job.id, { agentPaused: agent });
    pushLog(job.id, `${agent === "transcriber" ? AGENT_ALIASES.transcriber : AGENT_ALIASES.refiner}: paused — current requests will complete, new requests will wait`);
    
    res.json({ ok: true, message: `${agent} paused` });
  } catch (err) {
    return res.status(500).json({ error: err?.message || "failed to pause agent" });
  }
});

/**
 * POST /api/jobs/:id/resume-agent - Resume agent (transcriber or refiner)
 */
router.post("/:id/resume-agent", (req, res) => {
  const { id } = req.params;
  const { agent } = req.body; // "transcriber" or "refiner"
  
  // Validate UUID format
  const uuidRegex = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
  if (!uuidRegex.test(id)) {
    return res.status(400).json({ error: "invalid job id format" });
  }

  if (!agent || (agent !== "transcriber" && agent !== "refiner")) {
    return res.status(400).json({ error: "agent must be 'transcriber' or 'refiner'" });
  }

  const job = getJob(id);
  if (!job) {
    return res.status(404).json({ error: "job not found" });
  }

  try {
    const pauseFlagPath = path.join(job.dir, "pause_agent.flag");
    if (fs.existsSync(pauseFlagPath)) {
      const currentAgent = fs.readFileSync(pauseFlagPath, 'utf8').trim();
      if (currentAgent === agent) {
        fs.unlinkSync(pauseFlagPath);
        updateJobAndBroadcast(job.id, { agentPaused: null });
        pushLog(job.id, `${agent === "transcriber" ? AGENT_ALIASES.transcriber : AGENT_ALIASES.refiner}: resumed — new requests will be processed`);
      }
    }
    
    res.json({ ok: true, message: `${agent} resumed` });
  } catch (err) {
    return res.status(500).json({ error: err?.message || "failed to resume agent" });
  }
});

/**
 * POST /api/jobs/:id/skip-refiner - Skip refiner and mark job as done
 */
router.post("/:id/skip-refiner", (req, res) => {
  const job = getJob(req.params.id);
  if (!job) {
    return res.status(404).json({ error: "job not found" });
  }

  if (job.phase !== "awaiting_refiner") {
    return res.status(400).json({ 
      error: `Cannot skip refiner in phase '${job.phase}'.` 
    });
  }

  try {
    updateJobAndBroadcast(job.id, {
      status: "done",
      phase: "completed",
    });
    pushLog(job.id, "Refiner skipped — using raw transcript ✅");
    broadcast(job.id, { type: "done" });
    return res.json({ ok: true });
  } catch (err) {
    return res.status(500).json({ error: err?.message || "failed to skip refiner" });
  }
});

/**
 * GET /api/jobs/:id/chunks - Get list of all chunks with their paths
 */
router.get("/:id/chunks", (req, res) => {
  const job = getJob(req.params.id);
  if (!job) {
    return res.status(404).json({ error: "job not found" });
  }

  try {
    const chunkPaths = getAllChunkPaths(job);
    const chunks = Object.entries(chunkPaths).map(([idx, audioPath]) => ({
      idx: parseInt(idx, 10),
      audioPath,
      audioUrl: `/api/jobs/${job.id}/chunks/${idx}/audio`,
    }));
    return res.json({ chunks });
  } catch (err) {
    return res.status(500).json({ error: err?.message || "failed to get chunks" });
  }
});

/**
 * GET /api/jobs/:id/chunks/:idx/audio - Stream chunk audio file
 */
router.get("/:id/chunks/:idx/audio", (req, res) => {
  const job = getJob(req.params.id);
  if (!job) {
    return res.status(404).json({ error: "job not found" });
  }

  const idx = Number.parseInt(req.params.idx, 10);
  if (Number.isNaN(idx) || idx < 0) {
    return res.status(400).json({ error: "invalid chunk index" });
  }

  try {
    const audioPath = getChunkAudioPath(job, idx);
    if (!audioPath || !fs.existsSync(audioPath)) {
      return res.status(404).json({ error: `Audio file not found for chunk ${idx}` });
    }

    const stat = fs.statSync(audioPath);
    const ext = path.extname(audioPath).toLowerCase();
    
    // Set content type based on extension
    const contentTypes = {
      ".wav": "audio/wav",
      ".m4a": "audio/mp4",
      ".mp3": "audio/mpeg",
      ".ogg": "audio/ogg",
      ".flac": "audio/flac",
    };
    const contentType = contentTypes[ext] || "audio/mpeg";

    res.writeHead(200, {
      "Content-Type": contentType,
      "Content-Length": stat.size,
      "Accept-Ranges": "bytes",
    });

    const readStream = fs.createReadStream(audioPath);
    readStream.pipe(res);
  } catch (err) {
    return res.status(500).json({ error: err?.message || "failed to stream audio" });
  }
});

/**
 * GET /api/jobs/:id/stream - SSE stream for job updates
 */
router.get("/:id/stream", (req, res) => {
  const { id } = req.params;
  let job = getJob(id);

  // If job not in memory, try to restore it (same logic as GET /:id)
  if (!job) {
    const jobDir = path.join(RUNTIME_DIR, id);
    if (fs.existsSync(jobDir)) {
      // Restore job from filesystem (simplified version)
      let audioPath = null;
      try {
        const files = fs.readdirSync(jobDir);
        const audioFiles = files.filter((f) =>
          /\.(m4a|mp3|wav|ogg|flac)$/i.test(f)
        );
        if (audioFiles.length > 0) {
          audioPath = path.join(jobDir, audioFiles[0]);
        }
      } catch (err) {
        // Can't read directory
      }

      const transcriptPath = path.join(jobDir, "transcript.md");
      const transcriptFixedPath = path.join(jobDir, "transcript_fixed.md");
      const hasTranscript = fs.existsSync(transcriptPath);
      const hasFixedTranscript = fs.existsSync(transcriptFixedPath);
      
      // Find all transcript_fixed files
      const allFixedFiles = [];
      try {
        const files = fs.readdirSync(jobDir);
        allFixedFiles.push(...files
          .filter(f => f.startsWith("transcript_fixed") && f.endsWith(".md"))
          .map(f => `runtime/${id}/${f}`)
          .sort()
        );
      } catch (err) {
        // Can't read directory
      }

      let status = "done";
      let phase = "completed";
      if (!hasTranscript && allFixedFiles.length === 0) {
        status = "failed";
        phase = "idle";
      }

      const chunksDir = path.join(jobDir, "chunks");
      const cancelDir = path.join(jobDir, "cancel_signals");
      let totalChunks = 0;
      let completedChunks = [];
      let cancelledChunks = [];
      let failedChunks = [];
      
      try {
        if (fs.existsSync(chunksDir)) {
          const chunkFiles = fs.readdirSync(chunksDir);
          totalChunks = chunkFiles.filter((f) =>
            /\.(m4a|mp3|wav|ogg|flac)$/i.test(f)
          ).length;
          
          // Restore cancelled chunks from cancel_signals directory
          if (fs.existsSync(cancelDir)) {
            try {
              const cancelFiles = fs.readdirSync(cancelDir);
              for (const file of cancelFiles) {
                const match = file.match(/^cancel_(\d+)\.flag$/);
                if (match) {
                  const idx = parseInt(match[1], 10);
                  if (!isNaN(idx) && idx >= 0 && idx < totalChunks) {
                    cancelledChunks.push(idx);
                  }
                }
              }
            } catch (err) {
              // Can't read cancel directory
            }
          }
          
          // Determine completed chunks (all chunks that are not cancelled)
          for (let i = 0; i < totalChunks; i++) {
            if (!cancelledChunks.includes(i)) {
              completedChunks.push(i);
            }
          }
        }
      } catch (err) {
        // Can't read chunks directory
      }

      job = createJob({
        id: id,
        status: status,
        phase: phase,
        logs: [],
        dir: jobDir,
        cancelDir: path.join(jobDir, "cancel_signals"),
        chunks: {
          total: totalChunks,
          active: [],
          completed: completedChunks,
          cancelled: cancelledChunks,
          failed: failedChunks,
        },
        audio: audioPath,
        result: {
          transcript: hasTranscript ? `runtime/${id}/transcript.md` : undefined,
          transcriptFixed: hasFixedTranscript
            ? `runtime/${id}/transcript_fixed.md`
            : undefined,
          transcriptFixedAll: allFixedFiles.length > 0 ? allFixedFiles : undefined,
          rawJson: fs.existsSync(path.join(jobDir, "response.json"))
            ? `runtime/${id}/response.json`
            : undefined,
        },
      });
    }
  }

  if (!job) {
    res.writeHead(404);
    res.end();
    return;
  }

  res.writeHead(200, {
    "Content-Type": "text/event-stream",
    "Cache-Control": "no-cache",
    Connection: "keep-alive",
  });

  const send = (event) => {
    res.write(`data: ${JSON.stringify(event)}\n\n`);
  };

  // Ensure splitJobs is synced to chunks
  ensureSplitJobsField(job);

  // Send initial snapshot
  send({
    type: "snapshot",
    payload: {
      id: job.id,
      status: job.status,
      phase: job.phase,
      logs: job.logs,
      chunks: job.chunks,
      result: job.result,
    },
  });

  const unsubscribe = subscribe(id, send);

  req.on("close", () => {
    unsubscribe();
  });
});

/**
 * GET /api/jobs/:id/files - Get list of all files in a job directory
 */
router.get("/:id/files", (req, res) => {
  const { id } = req.params;
  
  // Validate UUID format
  const uuidRegex = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
  if (!uuidRegex.test(id)) {
    return res.status(400).json({ error: "invalid job id format" });
  }

  const jobDir = path.join(RUNTIME_DIR, id);

  try {
    if (!fs.existsSync(jobDir)) {
      return res.status(404).json({ error: "job directory not found" });
    }

    const files = {
      original: [],
      chunks: [],
      chunkJson: [],
      transcripts: [],
      intermediate: [],
      converted: [],
      splitChunks: [], // Sub-chunks from split operations
    };

    // Helper function to get audio duration using ffprobe
    const getAudioDuration = (filePath) => {
      try {
        const cmd = `ffprobe -v error -show_entries format=duration -of default=nw=1:nk=1 "${filePath}"`;
        const output = execSync(cmd, { encoding: 'utf8', timeout: 5000 });
        const duration = parseFloat(output.trim());
        return isNaN(duration) ? null : duration;
      } catch (err) {
        // ffprobe not available or file can't be read
        return null;
      }
    };

    // Helper function to count lines in text file
    const countLines = (filePath) => {
      try {
        const content = fs.readFileSync(filePath, 'utf8');
        // Count non-empty lines, or all lines if file is empty
        const lines = content.split('\n');
        return lines.length;
      } catch (err) {
        // File can't be read
        return null;
      }
    };

    // Helper function to scan directory
    const scanDir = (dirPath, baseUrl) => {
      if (!fs.existsSync(dirPath)) return [];
      
      const items = [];
      try {
        const entries = fs.readdirSync(dirPath, { withFileTypes: true });
        for (const entry of entries) {
          if (entry.isFile()) {
            const filePath = path.join(dirPath, entry.name);
            const stats = fs.statSync(filePath);
            const ext = path.extname(entry.name).toLowerCase();
            const isAudio = /\.(m4a|mp3|wav|ogg|flac)$/i.test(entry.name);
            const isText = /\.(txt|md|json|log|text)$/i.test(entry.name);
            
            const fileInfo = {
              name: entry.name,
              path: filePath,
              size: stats.size,
              url: `${baseUrl}/${entry.name}`,
            };

            // Add audio duration
            if (isAudio) {
              const duration = getAudioDuration(filePath);
              if (duration !== null) {
                fileInfo.duration = duration;
              }
            }

            // Add line count for text files
            if (isText) {
              const lineCount = countLines(filePath);
              if (lineCount !== null) {
                fileInfo.lineCount = lineCount;
              }
            }

            items.push(fileInfo);
          }
        }
      } catch (err) {
        // Directory doesn't exist or can't read
      }
      return items;
    };

    // Scan root directory for original file and transcripts
    const rootFiles = scanDir(jobDir, `/runtime/${id}`);
    for (const file of rootFiles) {
      const ext = path.extname(file.name).toLowerCase();
      if (/\.(m4a|mp3|wav|ogg|flac)$/i.test(file.name)) {
        files.original.push(file);
      } else if (file.name.includes("transcript") || file.name.endsWith(".md")) {
        files.transcripts.push(file);
      } else if (file.name.endsWith(".json")) {
        files.transcripts.push(file);
      }
    }

    // Scan chunks directory
    const chunksDir = path.join(jobDir, "chunks");
    const chunkFiles = scanDir(chunksDir, `/runtime/${id}/chunks`);
    chunkFiles.sort((a, b) => a.name.localeCompare(b.name));
    files.chunks = chunkFiles.map((file) => {
      // Try to extract chunk index from filename
      const match = file.name.match(/(\d+)/);
      const index = match ? parseInt(match[1], 10) : undefined;
      return { ...file, index };
    });

    // Scan chunks_json directory
    const chunksJsonDir = path.join(jobDir, "chunks_json");
    const chunkJsonFiles = scanDir(chunksJsonDir, `/runtime/${id}/chunks_json`);
    chunkJsonFiles.sort((a, b) => a.name.localeCompare(b.name));
    files.chunkJson = chunkJsonFiles.map((file) => {
      const match = file.name.match(/(\d+)/);
      const index = match ? parseInt(match[1], 10) : undefined;
      return { ...file, index };
    });

    // Scan intermediate_results directory
    const intermediateDir = path.join(jobDir, "intermediate_results");
    files.intermediate = scanDir(intermediateDir, `/runtime/${id}/intermediate_results`);

    // Scan converted_wav directory
    const convertedDir = path.join(jobDir, "converted_wav");
    files.converted = scanDir(convertedDir, `/runtime/${id}/converted_wav`);

    // Scan split_chunks directory for sub-chunks
    const splitChunksDir = path.join(jobDir, "split_chunks");
    if (fs.existsSync(splitChunksDir)) {
      try {
        const splitDirs = fs.readdirSync(splitChunksDir, { withFileTypes: true });
        for (const dirent of splitDirs) {
          if (dirent.isDirectory() && dirent.name.startsWith("chunk_")) {
            const parentIdx = parseInt(dirent.name.replace("chunk_", ""), 10);
            if (!isNaN(parentIdx)) {
              const subChunksDir = path.join(splitChunksDir, dirent.name, "sub_chunks");
              const resultsDir = path.join(splitChunksDir, dirent.name, "results");
              const subChunkFiles = scanDir(subChunksDir, `/runtime/${id}/split_chunks/${dirent.name}/sub_chunks`);
              
              // Check which sub-chunks have transcription results and add transcript files
              const transcribedSubIndices = new Set();
              if (fs.existsSync(resultsDir)) {
                const resultFiles = scanDir(resultsDir, `/runtime/${id}/split_chunks/${dirent.name}/results`);
                resultFiles.forEach((resultFile) => {
                  // Match pattern: sub_chunk_XX_result.json
                  const match = resultFile.name.match(/sub_chunk_(\d+)_result\.json$/);
                  if (match) {
                    const subIdx = parseInt(match[1], 10);
                    if (!isNaN(subIdx)) {
                      transcribedSubIndices.add(subIdx);
                      // Add transcript file to splitChunks
                      files.splitChunks.push({
                        ...resultFile,
                        parentIndex: parentIdx,
                        subIndex: subIdx,
                        hasTranscript: true,
                        isTranscript: true, // Mark as transcript file
                      });
                    }
                  }
                });
              }
              
              // Add parent index and sub-index to each audio file, and mark if transcribed
              subChunkFiles.forEach((file) => {
                // Extract sub-index from filename (e.g., _sub_00, _sub_01)
                const subMatch = file.name.match(/_sub_(\d+)/);
                const subIdx = subMatch ? parseInt(subMatch[1], 10) : undefined;
                files.splitChunks.push({
                  ...file,
                  parentIndex: parentIdx,
                  subIndex: subIdx,
                  hasTranscript: subIdx !== undefined && transcribedSubIndices.has(subIdx),
                  isTranscript: false, // Mark as audio file
                });
              });
            }
          }
        }
      } catch (err) {
        // Can't read split_chunks directory
      }
    }

    res.json({ 
      files,
      jobDir: jobDir // Include the full path to the job directory
    });
  } catch (err) {
    console.error(`[GET /api/jobs/${id}/files] Error:`, err);
    res.status(500).json({ error: err?.message || "Failed to get job files" });
  }
});

/**
 * GET /api/jobs/:id/files/:path - Get file content
 */
router.get("/:id/files/*", (req, res) => {
  const { id } = req.params;
  const filePath = req.params[0]; // Everything after /files/
  
  // Validate UUID format
  const uuidRegex = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
  if (!uuidRegex.test(id)) {
    return res.status(400).json({ error: "invalid job id format" });
  }

  const jobDir = path.join(RUNTIME_DIR, id);
  // Normalize the path to prevent directory traversal attacks
  const normalizedFilePath = path.normalize(filePath).replace(/^(\.\.(\/|\\|$))+/, '');
  const fullPath = path.join(jobDir, normalizedFilePath);

  try {
    // Security: ensure the file is within the job directory (use resolve to get absolute path)
    const resolvedJobDir = path.resolve(jobDir);
    const resolvedFullPath = path.resolve(fullPath);
    if (!resolvedFullPath.startsWith(resolvedJobDir)) {
      return res.status(403).json({ error: "access denied" });
    }

    if (!fs.existsSync(fullPath)) {
      return res.status(404).json({ error: "file not found" });
    }

    const stats = fs.statSync(fullPath);
    if (!stats.isFile()) {
      return res.status(400).json({ error: "not a file" });
    }

    // Check if it's a text file
    const ext = path.extname(fullPath).toLowerCase();
    const isTextFile = /\.(txt|md|json|log|text|flag)$/i.test(ext);
    
    if (!isTextFile) {
      return res.status(400).json({ error: "not a text file" });
    }

    const content = fs.readFileSync(fullPath, 'utf8');
    res.json({ content, path: filePath });
  } catch (err) {
    console.error(`[GET /api/jobs/${id}/files/*] Error:`, err);
    res.status(500).json({ error: err?.message || "failed to read file" });
  }
});

/**
 * PUT /api/jobs/:id/files/:path - Save file content
 */
router.put("/:id/files/*", express.text({ type: '*/*', limit: '50mb' }), (req, res) => {
  const { id } = req.params;
  const filePath = req.params[0]; // Everything after /files/
  
  // Validate UUID format
  const uuidRegex = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
  if (!uuidRegex.test(id)) {
    return res.status(400).json({ error: "invalid job id format" });
  }

  const jobDir = path.join(RUNTIME_DIR, id);
  // Normalize the path to prevent directory traversal attacks
  const normalizedFilePath = path.normalize(filePath).replace(/^(\.\.(\/|\\|$))+/, '');
  const fullPath = path.join(jobDir, normalizedFilePath);

  try {
    // Security: ensure the file is within the job directory (use resolve to get absolute path)
    const resolvedJobDir = path.resolve(jobDir);
    const resolvedFullPath = path.resolve(fullPath);
    if (!resolvedFullPath.startsWith(resolvedJobDir)) {
      return res.status(403).json({ error: "access denied" });
    }

    if (!fs.existsSync(fullPath)) {
      return res.status(404).json({ error: "file not found" });
    }

    const stats = fs.statSync(fullPath);
    if (!stats.isFile()) {
      return res.status(400).json({ error: "not a file" });
    }

    // Check if it's a text file
    const ext = path.extname(fullPath).toLowerCase();
    const isTextFile = /\.(txt|md|json|log|text|flag)$/i.test(ext);
    
    if (!isTextFile) {
      return res.status(400).json({ error: "not a text file" });
    }

    // Save the content
    fs.writeFileSync(fullPath, req.body, 'utf8');
    res.json({ ok: true, message: "file saved successfully" });
  } catch (err) {
    console.error(`[PUT /api/jobs/${id}/files/*] Error:`, err);
    res.status(500).json({ error: err?.message || "failed to save file" });
  }
});

/**
 * DELETE /api/jobs/:id - Delete a job and its directory
 */
router.delete("/:id", (req, res) => {
  const { id } = req.params;
  
  // Validate UUID format
  const uuidRegex = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
  if (!uuidRegex.test(id)) {
    return res.status(400).json({ error: "invalid job id format" });
  }

  const job = getJob(id);
  const jobDir = path.join(RUNTIME_DIR, id);

  try {
    // Check if directory exists
    if (!fs.existsSync(jobDir)) {
      return res.status(404).json({ error: "job directory not found" });
    }

    // Delete job from memory store if it exists
    if (job) {
      deleteJob(id);
    }

    // Delete the entire job directory recursively
    // This removes ALL files including:
    // - Original audio file
    // - chunks/ directory (audio chunks)
    // - chunks_json/ directory (JSON files for chunks)
    // - cache/ directory
    // - converted_wav/ directory
    // - intermediate_results/ directory
    // - cancel_signals/ directory
    // - All transcript files (transcript.md, transcript_fixed.md, response.json, etc.)
    // - All config files
    console.log(`[DELETE /api/jobs/${id}] Deleting directory: ${jobDir}`);
    fs.rmSync(jobDir, { recursive: true, force: true });
    console.log(`[DELETE /api/jobs/${id}] Directory deleted successfully`);

    res.json({ ok: true, message: "Job and all associated files deleted successfully" });
  } catch (err) {
    console.error(`[DELETE /api/jobs/${id}] Error:`, err);
    res.status(500).json({ error: err?.message || "Failed to delete job" });
  }
});

export default router;

