import fs from "fs";
import path from "path";
import {
  ROOT,
  AGENT01_DIR,
  AGENT03_DIR,
  PYTHON_BIN,
  AGENT_ALIASES,
} from "../config.js";
import { pushLog, updateJobAndBroadcast, broadcast } from "./broadcaster.js";
import { spawnAndStream } from "../utils/spawn.js";

/**
 * Run the full transcription + refinement pipeline.
 */
export async function runPipeline(job, jobDir) {
  updateJobAndBroadcast(job.id, { status: "running", phase: "transcriber" });

  const transcriptPath = path.join(jobDir, "transcript.md");
  const rawJsonPath = path.join(jobDir, "response.json");

  await runTranscriber(job, jobDir, transcriptPath, rawJsonPath);

  updateJobAndBroadcast(job.id, { phase: "refiner" });

  const refinedPath = path.join(jobDir, "transcript_fixed.md");
  await runRefiner(job, jobDir, transcriptPath, refinedPath);

  updateJobAndBroadcast(job.id, {
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

/**
 * Run the transcriber agent (agent01).
 */
async function runTranscriber(job, jobDir, transcriptPath, rawJsonPath) {
  const configPath = path.join(jobDir, "config_agent01.json");
  const defaultConfigPath = path.join(AGENT01_DIR, "config", "default.json");

  const config = JSON.parse(fs.readFileSync(defaultConfigPath, "utf-8"));

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

  pushLog(job.id, `${AGENT_ALIASES.transcriber}: starting`);

  await spawnAndStream({
    jobId: job.id,
    label: AGENT_ALIASES.transcriber,
    command: PYTHON_BIN,
    args: ["-m", "cli.main", "--config", configPath],
    cwd: AGENT01_DIR,
    env: {},
  });
}

/**
 * Run the refiner agent (agent03).
 */
async function runRefiner(job, jobDir, transcriptPath, refinedPath) {
  const configPath = path.join(jobDir, "config_agent03.json");
  const defaultConfigPath = path.join(AGENT03_DIR, "config", "default.json");

  const config = JSON.parse(fs.readFileSync(defaultConfigPath, "utf-8"));

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
      `${AGENT_ALIASES.refiner}: missing OPENAI_API_KEY (refiner will not start)`,
      "error"
    );
    throw new Error("Refiner missing OPENAI_API_KEY");
  }

  pushLog(job.id, `${AGENT_ALIASES.refiner}: starting`);
  pushLog(
    job.id,
    `${AGENT_ALIASES.refiner}: cmd=${PYTHON_BIN} args=[run_refiner.py] cwd=${AGENT03_DIR}`
  );

  // Generate runner script
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
    label: AGENT_ALIASES.refiner,
    command: PYTHON_BIN,
    args: [runnerPath],
    cwd: AGENT03_DIR,
    env: { OPENAI_API_KEY: apiKey },
  });
}

