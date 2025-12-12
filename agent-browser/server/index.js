import express from "express";
import multer from "multer";
import { spawn } from "child_process";
import fs from "fs";
import path from "path";
import { fileURLToPath } from "url";
import crypto from "crypto";

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);
const ROOT = path.resolve(__dirname, "..");
const AGENT01_DIR = path.resolve(ROOT, "..", "agent01");
const AGENT03_DIR = path.resolve(ROOT, "..", "agent03-trans-improver");
const RUNTIME_DIR = path.join(ROOT, "runtime");

const app = express();
const upload = multer({ dest: path.join(RUNTIME_DIR, "uploads") });
const PORT = process.env.PORT || 3001;
const PYTHON_BIN = process.env.PYTHON_BIN || "python";

/** In-memory job store; could be replaced with Redis/db later. */
const jobs = new Map();
/** SSE subscribers keyed by jobId. */
const subscribers = new Map();

/** Basic CORS for local dev (Vite runs on 5173). */
app.use((req, res, next) => {
  res.setHeader("Access-Control-Allow-Origin", "*");
  res.setHeader("Access-Control-Allow-Headers", "Content-Type");
  res.setHeader("Access-Control-Allow-Methods", "GET,POST,OPTIONS");
  if (req.method === "OPTIONS") {
    res.sendStatus(204);
    return;
  }
  next();
});

app.use(express.json());
fs.mkdirSync(RUNTIME_DIR, { recursive: true });
fs.mkdirSync(path.join(RUNTIME_DIR, "uploads"), { recursive: true });
app.use("/runtime", express.static(RUNTIME_DIR));

const aliases = {
  transcriber: "Transcriber Agent",
  refiner: "Refiner Agent",
};

app.post("/api/jobs", upload.single("file"), async (req, res) => {
  if (!req.file) {
    return res.status(400).json({ error: "file is required" });
  }

  const jobId = crypto.randomUUID();
  const jobDir = path.join(RUNTIME_DIR, jobId);
  fs.mkdirSync(jobDir, { recursive: true });

  const savedAudioPath = path.join(jobDir, req.file.originalname);
  fs.renameSync(req.file.path, savedAudioPath);

  const job = {
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
  };
  jobs.set(jobId, job);
  fs.mkdirSync(job.cancelDir, { recursive: true });

  res.json({ jobId });

  try {
    await runPipeline(job, jobDir);
  } catch (err) {
    pushLog(jobId, `Pipeline failed: ${err?.message || err}`, "error");
    updateJob(jobId, { status: "failed" });
  }
});

app.get("/api/jobs/:id", (req, res) => {
  const job = jobs.get(req.params.id);
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

app.post("/api/jobs/:id/chunks/:idx/cancel", (req, res) => {
  const job = jobs.get(req.params.id);
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
    pushLog(job.id, `[${aliases.transcriber}] requested cancel for chunk #${idx + 1}`);
    broadcast(job.id, { type: "chunk", payload: { status: "cancelled", idx } });
    return res.json({ ok: true });
  } catch (err) {
    return res.status(500).json({ error: err?.message || "failed to cancel" });
  }
});

app.get("/api/jobs/:id/stream", (req, res) => {
  const { id } = req.params;
  const job = jobs.get(id);
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

  // Send initial snapshot so UI can hydrate.
  send({
    type: "snapshot",
    payload: {
      status: job.status,
      phase: job.phase,
      logs: job.logs,
      chunks: job.chunks,
      result: job.result,
    },
  });

  const current = subscribers.get(id) || [];
  subscribers.set(id, [...current, send]);

  req.on("close", () => {
    const arr = subscribers.get(id) || [];
    subscribers.set(
      id,
      arr.filter((fn) => fn !== send),
    );
  });
});

/** Helpers */
function broadcast(jobId, event) {
  const listeners = subscribers.get(jobId) || [];
  listeners.forEach((fn) => fn(event));
}

function pushLog(jobId, message, level = "info") {
  const job = jobs.get(jobId);
  if (!job) return;
  const entry = { ts: Date.now(), level, message };
  job.logs.push(entry);
  // keep the log size reasonable
  if (job.logs.length > 800) {
    job.logs.splice(0, job.logs.length - 800);
  }
  broadcast(jobId, { type: "log", payload: entry });
}

function updateJob(jobId, patch) {
  const job = jobs.get(jobId);
  if (!job) return;
  Object.assign(job, patch);
  broadcast(jobId, { type: "status", payload: patch });
}

const CHUNK_MARKER = "@@CHUNK_EVENT";

function updateChunksState(jobId, payload) {
  const job = jobs.get(jobId);
  if (!job) return;
  if (!job.chunks) {
    job.chunks = { total: 0, active: [], completed: [], cancelled: [], failed: [] };
  }
  const state = job.chunks;
  if (typeof payload.total === "number" && payload.total >= 0) {
    state.total = payload.total;
  }
  if (typeof payload.idx !== "number") {
    broadcast(jobId, { type: "chunk", payload });
    return;
  }
  const idx = payload.idx;
  const remove = (arr) => {
    const pos = arr.indexOf(idx);
    if (pos >= 0) arr.splice(pos, 1);
  };
  remove(state.active);
  remove(state.completed);
  remove(state.cancelled);
  remove(state.failed);

  switch (payload.status) {
    case "started":
      state.active.push(idx);
      break;
    case "completed":
      state.completed.push(idx);
      break;
    case "cancelled":
      state.cancelled.push(idx);
      break;
    case "failed":
      state.failed.push(idx);
      break;
    default:
      break;
  }
  broadcast(jobId, { type: "chunk", payload });
}

function tryProcessChunkEvent(jobId, line) {
  const markerIndex = line.indexOf(CHUNK_MARKER);
  if (markerIndex === -1) return;
  const data = line.slice(markerIndex + CHUNK_MARKER.length).trim();
  try {
    const payload = JSON.parse(data);
    updateChunksState(jobId, payload);
  } catch (err) {
    console.warn("Failed to parse chunk event", err);
  }
}

async function runPipeline(job, jobDir) {
  updateJob(job.id, { status: "running", phase: "transcriber" });
  const transcriptPath = path.join(jobDir, "transcript.md");
  const rawJsonPath = path.join(jobDir, "response.json");

  await runTranscriber(job, jobDir, transcriptPath, rawJsonPath);

  updateJob(job.id, { phase: "refiner" });
  const refinedPath = path.join(jobDir, "transcript_fixed.md");

  await runRefiner(job, jobDir, transcriptPath, refinedPath);

  updateJob(job.id, {
    status: "done",
    phase: "completed",
    result: {
      transcript: path.relative(ROOT, transcriptPath).replace(/\\/g, "/"),
      transcriptFixed: path.relative(ROOT, refinedPath).replace(/\\/g, "/"),
      rawJson: path.relative(ROOT, rawJsonPath).replace(/\\/g, "/"),
    },
  });
  pushLog(job.id, "Pipeline finished ✅");
  broadcast(job.id, { type: "done" });
}

async function runTranscriber(job, jobDir, transcriptPath, rawJsonPath) {
  const configPath = path.join(jobDir, "config_agent01.json");
  const config = JSON.parse(
    fs.readFileSync(path.join(AGENT01_DIR, "config", "default.json"), "utf-8"),
  );

  config.file = job.audio;
  config.md_output_path = transcriptPath;
  config.raw_json_output_path = rawJsonPath;
  config.cache_dir = path.join(jobDir, "cache");
  config.convert_to_wav = true;
  config.wav_output_dir = path.join(jobDir, "converted_wav");
  config.save_intermediate_results = true;
  config.intermediate_results_dir = path.join(jobDir, "intermediate_results");
  config.cancel_dir = job.cancelDir;
  config.clean_before_run = false;
  fs.writeFileSync(configPath, JSON.stringify(config, null, 2));

  pushLog(job.id, `${aliases.transcriber}: starting`);
  await spawnAndStream({
    jobId: job.id,
    label: aliases.transcriber,
    command: PYTHON_BIN,
    args: ["-m", "cli.main", "--config", configPath],
    cwd: AGENT01_DIR,
    env: {},
  });
}

async function runRefiner(job, jobDir, transcriptPath, refinedPath) {
  const configPath = path.join(jobDir, "config_agent03.json");
  const config = JSON.parse(
    fs.readFileSync(path.join(AGENT03_DIR, "config", "default.json"), "utf-8"),
  );

  config.input_file = transcriptPath;
  config.output_file = refinedPath;
  config.intermediate_dir = path.join(jobDir, "intermediate_fixes");
  config.save_intermediate = true;
  fs.writeFileSync(configPath, JSON.stringify(config, null, 2));

  const apiKey =
    process.env.OPENAI_API_KEY ||
    config.openai_api_key ||
    process.env.AGENT03_OPENAI_API_KEY;
  if (!apiKey) {
    pushLog(
      job.id,
      `${aliases.refiner}: missing OPENAI_API_KEY (refiner will not start)`,
      "error",
    );
    throw new Error("Refiner missing OPENAI_API_KEY");
  }

  pushLog(job.id, `${aliases.refiner}: starting`);
  pushLog(
    job.id,
    `${aliases.refiner}: cmd=${PYTHON_BIN} args=[${["run_refiner.py"].join(", ")}] cwd=${AGENT03_DIR}`,
  );
  const runnerPath = path.join(jobDir, "run_refiner.py");
  const runnerSource = `
import sys, os
sys.path.insert(0, r"${AGENT03_DIR.replace(/\\/g, "/")}")
# Load environment variables from .env if available
try:
    from dotenv import load_dotenv
    load_dotenv()
except Exception:
    pass
from core.config import Config
from services.fixer import TranscriptFixer

cfg = Config.from_file(r"${configPath.replace(/\\/g, "/")}")
api_key = cfg.get("openai_api_key") or os.environ.get("OPENAI_API_KEY")
if not api_key:
    raise SystemExit("OPENAI_API_KEY is missing")

fixer = TranscriptFixer(
    api_key=api_key,
    model=cfg.get("model"),
    temperature=cfg.get("temperature"),
    base_url=cfg.get("openai_base_url"),
    organization=cfg.get("openai_organization"),
    prompt_file=cfg.get("prompt_file"),
)

fixer.fix_transcript_file(
    input_path=cfg.get("input_file"),
    output_path=cfg.get("output_file"),
    batch_size=cfg.get("batch_size"),
    context_lines=cfg.get("context_lines"),
    save_intermediate=cfg.get("save_intermediate"),
    intermediate_dir=cfg.get("intermediate_dir"),
)
`;
  fs.writeFileSync(runnerPath, runnerSource);

  await spawnAndStream({
    jobId: job.id,
    label: aliases.refiner,
    command: PYTHON_BIN,
    args: [runnerPath],
    cwd: AGENT03_DIR,
    env: {
      OPENAI_API_KEY: apiKey,
    },
  });
}

function spawnAndStream({ jobId, label, command, args, cwd, env = {} }) {
  return new Promise((resolve, reject) => {
    const proc = spawn(command, args, {
      cwd,
      env: {
        ...process.env,
        PYTHONIOENCODING: "utf-8",
        PYTHONUTF8: "1",
        ...env,
      },
      stdio: ["ignore", "pipe", "pipe"],
    });

    pushLog(jobId, `${label}: pid ${proc.pid} started (cwd=${cwd})`);

    const prefix = `[${label}]`;
    const handleStream = (data, level = "info") => {
      const text = data.toString();
      text.split(/\r?\n/).forEach((line) => {
        if (!line.trim()) return;
        tryProcessChunkEvent(jobId, line);
        pushLog(jobId, `${prefix} ${line.trimEnd()}`, level);
      });
    };

    proc.stdout.on("data", (data) => handleStream(data, "info"));
    proc.stderr.on("data", (data) => handleStream(data, "warn"));
    proc.on("error", (err) => {
      pushLog(jobId, `${prefix} failed to start: ${err.message}`, "error");
      reject(err);
    });
    proc.on("close", (code) => {
      pushLog(jobId, `${label} exited with code ${code}`);
      if (code === 0) {
        pushLog(jobId, `${prefix} finished`);
        resolve(null);
      } else {
        const error = new Error(`${label} exited with code ${code}`);
        pushLog(jobId, error.message, "error");
        reject(error);
      }
    });
  });
}

app.listen(PORT, () => {
  console.log(`API server listening on http://localhost:${PORT}`);
});

