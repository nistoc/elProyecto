import express from "express";
import multer from "multer";
import fs from "fs";
import path from "path";
import crypto from "crypto";
import { RUNTIME_DIR, UPLOADS_DIR, AGENT_ALIASES } from "../config.js";
import { createJob, getJob } from "../services/jobStore.js";
import {
  broadcast,
  subscribe,
  pushLog,
  updateJobAndBroadcast,
} from "../services/broadcaster.js";
import { runPipeline } from "../services/pipeline.js";

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
 * GET /api/jobs/:id - Get job status
 */
router.get("/:id", (req, res) => {
  const job = getJob(req.params.id);
  if (!job) {
    return res.status(404).json({ error: "job not found" });
  }

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
 * GET /api/jobs/:id/stream - SSE stream for job updates
 */
router.get("/:id/stream", (req, res) => {
  const { id } = req.params;
  const job = getJob(id);

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

export default router;

