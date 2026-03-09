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
import { getJob } from "./jobStore.js";

/**
 * Run the full transcription + refinement pipeline.
 * Now stops after transcription and waits for manual refiner start.
 */
export async function runPipeline(job, jobDir) {
  updateJobAndBroadcast(job.id, { status: "running", phase: "transcriber" });

  const transcriptPath = path.join(jobDir, "transcript.md");
  const rawJsonPath = path.join(jobDir, "response.json");

  await runTranscriber(job, jobDir, transcriptPath, rawJsonPath);

  // Store paths in job for later use
  job.transcriptPath = transcriptPath;
  job.rawJsonPath = rawJsonPath;
  job.refinedPath = path.join(jobDir, "transcript_fixed.md");

  // Update to awaiting_refiner phase - user must manually start refiner
  updateJobAndBroadcast(job.id, {
    status: "running",
    phase: "awaiting_refiner",
    result: {
      transcript: path.relative(ROOT, transcriptPath).replace(/\\/g, "/"),
      rawJson: path.relative(ROOT, rawJsonPath).replace(/\\/g, "/"),
    },
  });

  pushLog(job.id, "Transcription complete ✅ — Click 'Start Refiner' to continue or download transcript now");
}

/**
 * Run only the refiner stage (called manually by user).
 * @param {Object} job - Job object
 * @param {string} [outputFileName] - Optional custom output filename. If not provided, generates unique name.
 */
export async function runRefinerStage(job, outputFileName = null) {
  const jobDir = job.dir;
  const transcriptPath = job.transcriptPath || path.join(jobDir, "transcript.md");

  if (!fs.existsSync(transcriptPath)) {
    throw new Error("Transcript file not found. Complete transcription first.");
  }

  // Generate unique output filename if not provided
  let refinedPath;
  if (outputFileName) {
    refinedPath = path.join(jobDir, outputFileName);
  } else {
    // Find existing transcript_fixed files and generate next number
    const existingFiles = fs.readdirSync(jobDir).filter(f => 
      f.startsWith("transcript_fixed") && f.endsWith(".md")
    );
    
    // Extract numbers from existing files (transcript_fixed_1.md, transcript_fixed_2.md, etc.)
    // Also check for transcript_fixed.md (without number) - treat it as 0
    const numbers = existingFiles
      .map(f => {
        if (f === "transcript_fixed.md") {
          return 0; // Existing file without number
        }
        const match = f.match(/transcript_fixed[_-](\d+)\.md$/);
        return match ? parseInt(match[1], 10) : -1;
      })
      .filter(n => n >= 0);
    
    const nextNumber = numbers.length > 0 ? Math.max(...numbers) + 1 : 1;
    refinedPath = path.join(jobDir, `transcript_fixed_${nextNumber}.md`);
  }

  updateJobAndBroadcast(job.id, { phase: "refiner" });

  await runRefiner(job, jobDir, transcriptPath, refinedPath);

  // Update result to include all transcript_fixed files
  const allFixedFiles = fs.readdirSync(jobDir)
    .filter(f => f.startsWith("transcript_fixed") && f.endsWith(".md"))
    .map(f => path.relative(ROOT, path.join(jobDir, f)).replace(/\\/g, "/"))
    .sort();

  updateJobAndBroadcast(job.id, {
    status: "done",
    phase: "completed",
    result: {
      ...job.result,
      transcriptFixedAll: allFixedFiles,
    },
  });

  pushLog(job.id, "Refinement complete ✅");
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
  // Store chunks in job directory for split functionality
  config.split_workdir = path.join(jobDir, "chunks");
  config.per_chunk_json_dir = path.join(jobDir, "chunks_json");

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
    args: ["-u", runnerPath],  // -u flag for unbuffered output
    cwd: AGENT03_DIR,
    env: { 
      OPENAI_API_KEY: apiKey,
      PYTHONUNBUFFERED: "1",
    },
  });
}

/**
 * Rebuild transcript.md from individual chunk transcripts and split chunk transcripts.
 * This allows users to edit individual chunk transcript files and then rebuild the main transcript.
 * @param {Object} job - Job object
 */
export async function rebuildTranscript(job) {
  const jobDir = job.dir;
  const transcriptPath = path.join(jobDir, "transcript.md");

  pushLog(job.id, `[${AGENT_ALIASES.transcriber}] Rebuilding transcript.md from chunk transcripts...`);

  // Path to the rebuild script
  const rebuildScriptPath = path.join(AGENT01_DIR, "cli", "rebuild_transcript.py");

  if (!fs.existsSync(rebuildScriptPath)) {
    throw new Error(`Rebuild script not found: ${rebuildScriptPath}`);
  }

  // Generate runner script
  const runnerPath = path.join(jobDir, "run_rebuild_transcript.py");
  const runnerSource = `
import sys, os
sys.path.insert(0, r"${AGENT01_DIR.replace(/\\/g, "/")}")
# Load environment variables from .env if available
try:
    from dotenv import load_dotenv
    load_dotenv()
except Exception:
    pass

from cli.rebuild_transcript import rebuild_transcript

try:
    rebuild_transcript(r"${jobDir.replace(/\\/g, "/")}", r"${transcriptPath.replace(/\\/g, "/")}")
    print("[SUCCESS] Transcript rebuilt successfully")
except Exception as e:
    print(f"[ERROR] Failed to rebuild transcript: {e}")
    import traceback
    traceback.print_exc()
    sys.exit(1)
`;
  fs.writeFileSync(runnerPath, runnerSource);

  await spawnAndStream({
    jobId: job.id,
    label: AGENT_ALIASES.transcriber,
    command: PYTHON_BIN,
    args: ["-u", runnerPath],
    cwd: AGENT01_DIR,
    env: {
      PYTHONUNBUFFERED: "1",
    },
  });

  // Update job to reflect the rebuilt transcript
  if (fs.existsSync(transcriptPath)) {
    job.transcriptPath = transcriptPath;
    pushLog(job.id, `[${AGENT_ALIASES.transcriber}] ✅ Transcript rebuilt successfully`);
  } else {
    throw new Error("Transcript file was not created after rebuild");
  }
}
