import fs from "fs";
import path from "path";
import {
  ROOT,
  AGENT01_DIR,
  PYTHON_BIN,
  AGENT_ALIASES,
} from "../config.js";
import { getJob } from "./jobStore.js";
import { pushLog, broadcast } from "./broadcaster.js";
import { spawnAndStream } from "../utils/spawn.js";

/**
 * Marker for split events (parsed by the server from Python stdout).
 */
export const SPLIT_MARKER = "@@SPLIT_EVENT";

/**
 * Initialize splitJobs tracking on a job if not present.
 */
export function ensureSplitJobsField(job) {
  if (!job.splitJobs) {
    job.splitJobs = {};
  }
  // Also ensure chunks.splitJobs mirrors this
  if (job.chunks && !job.chunks.splitJobs) {
    job.chunks.splitJobs = job.splitJobs;
  }
}

/**
 * Get the path to a chunk's audio file.
 * Chunks are stored in the job's chunks folder.
 * The naming pattern is typically {base}_part_XXX.wav
 */
export function getChunkAudioPath(job, chunkIdx) {
  const jobDir = job.dir;
  
  // First check if we have a stored mapping of chunk paths
  if (job.chunkPaths && job.chunkPaths[chunkIdx]) {
    const storedPath = job.chunkPaths[chunkIdx];
    if (fs.existsSync(storedPath)) {
      return storedPath;
    }
  }
  
  // Check the chunks directory for files
  const chunksDir = path.join(jobDir, "chunks");
  if (fs.existsSync(chunksDir)) {
    const files = fs.readdirSync(chunksDir);
    
    // Filter audio files and sort them by the number in filename
    const audioFiles = files
      .filter(f => /\.(wav|m4a|mp3|ogg|flac)$/i.test(f))
      .sort((a, b) => {
        // Extract the last number from filenames (e.g., _part_005.wav -> 5)
        const numA = parseInt((a.match(/_(\d+)\.[^.]+$/) || ["", "0"])[1], 10);
        const numB = parseInt((b.match(/_(\d+)\.[^.]+$/) || ["", "0"])[1], 10);
        return numA - numB;
      });
    
    if (audioFiles[chunkIdx]) {
      return path.join(chunksDir, audioFiles[chunkIdx]);
    }
  }
  
  // If no chunks directory, check if file was too small for splitting
  // In that case, use the converted WAV file directly
  const convertedDir = path.join(jobDir, "converted_wav");
  if (fs.existsSync(convertedDir) && chunkIdx === 0) {
    const wavFiles = fs.readdirSync(convertedDir).filter(f => f.endsWith(".wav"));
    if (wavFiles.length === 1) {
      return path.join(convertedDir, wavFiles[0]);
    }
  }
  
  // Try to find the original audio file
  if (job.audio && fs.existsSync(job.audio) && chunkIdx === 0) {
    return job.audio;
  }
  
  return null;
}

/**
 * Get all chunk audio paths for a job.
 * Returns a map of chunkIdx -> audioPath
 */
export function getAllChunkPaths(job) {
  const jobDir = job.dir;
  const result = {};
  
  // Check the chunks directory
  const chunksDir = path.join(jobDir, "chunks");
  if (fs.existsSync(chunksDir)) {
    const files = fs.readdirSync(chunksDir);
    const audioFiles = files
      .filter(f => /\.(wav|m4a|mp3|ogg|flac)$/i.test(f))
      .sort((a, b) => {
        const numA = parseInt((a.match(/_(\d+)\.[^.]+$/) || ["", "0"])[1], 10);
        const numB = parseInt((b.match(/_(\d+)\.[^.]+$/) || ["", "0"])[1], 10);
        return numA - numB;
      });
    
    audioFiles.forEach((file, idx) => {
      result[idx] = path.join(chunksDir, file);
    });
  }
  
  return result;
}

/**
 * Create directories for split operation.
 */
function ensureSplitDirs(jobDir, parentIdx) {
  const splitDir = path.join(jobDir, "split_chunks", `chunk_${parentIdx}`);
  const subChunksDir = path.join(splitDir, "sub_chunks");
  const resultsDir = path.join(splitDir, "results");
  
  fs.mkdirSync(subChunksDir, { recursive: true });
  fs.mkdirSync(resultsDir, { recursive: true });
  
  return { splitDir, subChunksDir, resultsDir };
}

/**
 * Broadcast a split event to SSE subscribers.
 */
export function broadcastSplitEvent(jobId, payload) {
  broadcast(jobId, { type: "split", payload });
}

/**
 * Start the split and re-transcribe process for a failed/cancelled chunk.
 * 
 * @param {Object} job - The job object
 * @param {number} chunkIdx - Index of the chunk to split
 * @param {number} parts - Number of parts to split into (2, 3, or 4)
 */
export async function startSplitJob(job, chunkIdx, parts) {
  ensureSplitJobsField(job);
  
  const chunkAudioPath = getChunkAudioPath(job, chunkIdx);
  if (!chunkAudioPath) {
    throw new Error(`Could not find audio file for chunk ${chunkIdx}`);
  }
  
  const { splitDir, subChunksDir, resultsDir } = ensureSplitDirs(job.dir, chunkIdx);
  
  // Initialize split job tracking
  job.splitJobs[chunkIdx] = {
    parentIdx: chunkIdx,
    parts,
    status: "splitting",
    subChunks: [],
    chunkAudioPath,
    subChunksDir,
    resultsDir,
  };
  
  // Sync to chunks.splitJobs
  if (job.chunks) {
    job.chunks.splitJobs = job.splitJobs;
  }
  
  // Broadcast start
  broadcastSplitEvent(job.id, {
    type: "split_started",
    parentIdx: chunkIdx,
    parts,
  });
  
  pushLog(job.id, `[${AGENT_ALIASES.transcriber}] Starting split of chunk #${chunkIdx + 1} into ${parts} parts`);
  
  // Create config for the split operation
  const splitConfigPath = path.join(splitDir, "split_config.json");
  
  // Paths to main transcript files for integration
  const mainTranscriptPath = path.join(job.dir, "transcript.md");
  const mainJsonPath = path.join(job.dir, "response.json");
  
  // Get the chunk offset from intermediate results if available
  let parentChunkOffset = 0.0;
  const intermediateDir = path.join(job.dir, "intermediate_results");
  if (fs.existsSync(intermediateDir)) {
    // Try to find the intermediate result for this chunk to get its offset
    const files = fs.readdirSync(intermediateDir);
    const chunkResultFile = files.find(f => 
      f.includes(`chunk_${String(chunkIdx).padStart(3, "0")}`) && f.endsWith(".json")
    );
    if (chunkResultFile) {
      try {
        const chunkData = JSON.parse(
          fs.readFileSync(path.join(intermediateDir, chunkResultFile), "utf-8")
        );
        parentChunkOffset = chunkData.offset || 0.0;
      } catch (e) {
        console.warn("Could not read chunk offset:", e.message);
      }
    }
  }
  
  const splitConfig = {
    operation: "split_and_transcribe",
    chunk_audio_path: chunkAudioPath,
    parent_chunk_idx: chunkIdx,
    parent_chunk_offset: parentChunkOffset,
    parts,
    sub_chunks_dir: subChunksDir,
    results_dir: resultsDir,
    cancel_dir: path.join(splitDir, "cancel_signals"),
    // Paths to main files for integration
    main_transcript_path: mainTranscriptPath,
    main_json_path: mainJsonPath,
    // Copy relevant settings from the main job config
    openai_api_key: process.env.OPENAI_API_KEY,
    model: "whisper-1",
    language: "es",
    response_format: "verbose_json",
    timestamp_granularities: ["segment"],
  };
  
  // Try to load additional settings from the job's config
  const mainConfigPath = path.join(job.dir, "config_agent01.json");
  if (fs.existsSync(mainConfigPath)) {
    try {
      const mainConfig = JSON.parse(fs.readFileSync(mainConfigPath, "utf-8"));
      splitConfig.model = mainConfig.model || splitConfig.model;
      splitConfig.language = mainConfig.language || splitConfig.language;
      
      // Resolve API key - don't use "env:..." strings, use actual env value
      if (mainConfig.openai_api_key && !mainConfig.openai_api_key.startsWith("env:")) {
        splitConfig.openai_api_key = mainConfig.openai_api_key;
      }
      // Always prefer actual environment variable
      if (process.env.OPENAI_API_KEY) {
        splitConfig.openai_api_key = process.env.OPENAI_API_KEY;
      }
      
      splitConfig.openai_base_url = mainConfig.openai_base_url;
      splitConfig.openai_organization = mainConfig.openai_organization;
      splitConfig.temperature = mainConfig.temperature;
      splitConfig.prompt = mainConfig.prompt;
    } catch (err) {
      console.warn("Could not load main config:", err.message);
    }
  }
  
  // Ensure we have a valid API key BEFORE writing config
  const apiKey = process.env.OPENAI_API_KEY || splitConfig.openai_api_key;
  if (!apiKey || apiKey.startsWith("env:")) {
    throw new Error("OPENAI_API_KEY environment variable is not set");
  }
  
  // Write the resolved API key to config (not the env: placeholder)
  splitConfig.openai_api_key = apiKey;
  
  fs.writeFileSync(splitConfigPath, JSON.stringify(splitConfig, null, 2));
  fs.mkdirSync(splitConfig.cancel_dir, { recursive: true });
  
  // Run the split operation via Python
  try {
    await spawnAndStream({
      jobId: job.id,
      label: `${AGENT_ALIASES.transcriber} (split #${chunkIdx + 1})`,
      command: PYTHON_BIN,
      args: ["-m", "cli.split", "--config", splitConfigPath],
      cwd: AGENT01_DIR,
      env: { OPENAI_API_KEY: apiKey },
      onStdout: (line) => {
        // Parse split events from stdout
        tryProcessSplitEvent(job.id, chunkIdx, line);
      },
    });
    
    // Mark as completed if we get here
    if (job.splitJobs[chunkIdx]) {
      job.splitJobs[chunkIdx].status = "completed";
    }
    
    broadcastSplitEvent(job.id, {
      type: "split_completed",
      parentIdx: chunkIdx,
    });
    
    pushLog(job.id, `[${AGENT_ALIASES.transcriber}] Split of chunk #${chunkIdx + 1} completed successfully`);
    
  } catch (err) {
    if (job.splitJobs[chunkIdx]) {
      job.splitJobs[chunkIdx].status = "failed";
      job.splitJobs[chunkIdx].error = err.message;
    }
    
    broadcastSplitEvent(job.id, {
      type: "split_failed",
      parentIdx: chunkIdx,
      error: err.message,
    });
    
    pushLog(job.id, `[${AGENT_ALIASES.transcriber}] Split of chunk #${chunkIdx + 1} failed: ${err.message}`, "error");
    throw err;
  }
}

/**
 * Try to parse and process a split event from stdout line.
 */
export function tryProcessSplitEvent(jobId, parentIdx, line) {
  const markerIndex = line.indexOf(SPLIT_MARKER);
  if (markerIndex === -1) return false;
  
  const data = line.slice(markerIndex + SPLIT_MARKER.length).trim();
  try {
    const payload = JSON.parse(data);
    payload.parentIdx = parentIdx;
    
    // Update job state
    const job = getJob(jobId);
    if (job && job.splitJobs && job.splitJobs[parentIdx]) {
      const splitJob = job.splitJobs[parentIdx];
      
      switch (payload.event) {
        case "sub_chunks_created":
          splitJob.status = "transcribing";
          splitJob.subChunks = payload.subChunks || [];
          break;
          
        case "sub_chunk_started":
          if (splitJob.subChunks[payload.subIdx]) {
            splitJob.subChunks[payload.subIdx].status = "started";
          }
          break;
          
        case "sub_chunk_completed":
          if (splitJob.subChunks[payload.subIdx]) {
            splitJob.subChunks[payload.subIdx].status = "completed";
          }
          break;
          
        case "sub_chunk_failed":
          if (splitJob.subChunks[payload.subIdx]) {
            splitJob.subChunks[payload.subIdx].status = "failed";
          }
          break;
          
        case "sub_chunk_cancelled":
          if (splitJob.subChunks[payload.subIdx]) {
            splitJob.subChunks[payload.subIdx].status = "cancelled";
          }
          break;
          
        case "merging":
          splitJob.status = "merging";
          break;
          
        case "merge_completed":
          splitJob.status = "completed";
          splitJob.mergedText = payload.mergedText;
          break;
      }
      
      // Sync to chunks.splitJobs
      if (job.chunks) {
        job.chunks.splitJobs = job.splitJobs;
      }
    }
    
    broadcastSplitEvent(jobId, {
      type: "split_progress",
      parentIdx,
      ...payload,
    });
    
    return true;
  } catch (err) {
    console.warn("Failed to parse split event:", err);
    return false;
  }
}

/**
 * Cancel a sub-chunk within a split job.
 */
export function cancelSubChunk(job, parentIdx, subIdx) {
  ensureSplitJobsField(job);
  
  const splitJob = job.splitJobs[parentIdx];
  if (!splitJob) {
    throw new Error(`No split job found for chunk ${parentIdx}`);
  }
  
  // Create cancel flag for sub-chunk
  const cancelDir = path.join(job.dir, "split_chunks", `chunk_${parentIdx}`, "cancel_signals");
  fs.mkdirSync(cancelDir, { recursive: true });
  
  const flagPath = path.join(cancelDir, `cancel_sub_${subIdx}.flag`);
  fs.writeFileSync(flagPath, "cancelled");
  
  // Update state
  if (splitJob.subChunks[subIdx]) {
    splitJob.subChunks[subIdx].status = "cancelled";
  }
  
  pushLog(job.id, `[${AGENT_ALIASES.transcriber}] Requested cancel for sub-chunk #${parentIdx + 1}.${subIdx + 1}`);
  
  broadcastSplitEvent(job.id, {
    type: "split_progress",
    parentIdx,
    event: "sub_chunk_cancelled",
    subIdx,
  });
}

/**
 * Retranscribe a specific sub-chunk from a split job.
 * 
 * @param {Object} job - The job object
 * @param {number} parentIdx - Index of the parent chunk
 * @param {number} subIdx - Index of the sub-chunk to retranscribe
 */
export async function retranscribeSubChunk(job, parentIdx, subIdx) {
  ensureSplitJobsField(job);
  
  const splitDir = path.join(job.dir, "split_chunks", `chunk_${parentIdx}`);
  const subChunksDir = path.join(splitDir, "sub_chunks");
  const resultsDir = path.join(splitDir, "results");
  
  if (!fs.existsSync(subChunksDir)) {
    throw new Error(`Sub-chunks directory not found for chunk ${parentIdx}`);
  }
  
  // Find the sub-chunk audio file
  const subChunkFiles = fs.readdirSync(subChunksDir).filter(f => 
    /_sub_\d+\.(wav|m4a|mp3|ogg|flac)$/i.test(f)
  );
  
  // Match sub-chunk file by index (e.g., _sub_00, _sub_01)
  const subChunkFile = subChunkFiles.find(f => {
    const match = f.match(/_sub_(\d+)/);
    return match && parseInt(match[1], 10) === subIdx;
  });
  
  if (!subChunkFile) {
    throw new Error(`Sub-chunk ${subIdx} audio file not found`);
  }
  
  const subChunkAudioPath = path.join(subChunksDir, subChunkFile);
  
  // Create config for single sub-chunk transcription
  const retranscribeConfigPath = path.join(splitDir, `retranscribe_sub_${subIdx}_config.json`);
  
  // Load main job config for settings
  const mainConfigPath = path.join(job.dir, "config_agent01.json");
  let apiKey = process.env.OPENAI_API_KEY;
  let model = "whisper-1";
  let language = "es";
  let baseUrl = null;
  let organization = null;
  let temperature = null;
  let prompt = null;
  
  if (fs.existsSync(mainConfigPath)) {
    try {
      const mainConfig = JSON.parse(fs.readFileSync(mainConfigPath, "utf-8"));
      if (mainConfig.openai_api_key && !mainConfig.openai_api_key.startsWith("env:")) {
        apiKey = mainConfig.openai_api_key;
      }
      model = mainConfig.model || model;
      language = mainConfig.language || language;
      baseUrl = mainConfig.openai_base_url;
      organization = mainConfig.openai_organization;
      temperature = mainConfig.temperature;
      prompt = mainConfig.prompt;
    } catch (err) {
      console.warn("Could not load main config:", err.message);
    }
  }
  
  if (!apiKey || apiKey.startsWith("env:")) {
    throw new Error("OPENAI_API_KEY environment variable is not set");
  }
  
  const retranscribeConfig = {
    operation: "retranscribe_sub_chunk",
    sub_chunk_audio_path: subChunkAudioPath,
    parent_chunk_idx: parentIdx,
    sub_chunk_idx: subIdx,
    results_dir: resultsDir,
    openai_api_key: apiKey,
    model,
    language,
    response_format: "verbose_json",
    timestamp_granularities: ["segment"],
  };
  
  if (baseUrl) retranscribeConfig.openai_base_url = baseUrl;
  if (organization) retranscribeConfig.openai_organization = organization;
  if (temperature !== null) retranscribeConfig.temperature = temperature;
  if (prompt) retranscribeConfig.prompt = prompt;
  
  fs.writeFileSync(retranscribeConfigPath, JSON.stringify(retranscribeConfig, null, 2));
  
  // Update split job state
  if (!job.splitJobs[parentIdx]) {
    job.splitJobs[parentIdx] = {
      parentIdx,
      parts: 0, // Unknown, will be determined from files
      status: "transcribing",
      subChunks: [],
    };
  }
  
  // Ensure sub-chunk entry exists
  if (!job.splitJobs[parentIdx].subChunks[subIdx]) {
    job.splitJobs[parentIdx].subChunks[subIdx] = {
      idx: subIdx,
      status: "pending",
      audioPath: subChunkAudioPath,
    };
  }
  
  // Mark as started
  job.splitJobs[parentIdx].subChunks[subIdx].status = "started";
  
  // Sync to chunks.splitJobs
  if (job.chunks) {
    job.chunks.splitJobs = job.splitJobs;
  }
  
  broadcastSplitEvent(job.id, {
    type: "split_progress",
    parentIdx,
    event: "sub_chunk_started",
    subIdx,
  });
  
  pushLog(job.id, `[${AGENT_ALIASES.transcriber}] Retranscribing sub-chunk #${parentIdx + 1}.${subIdx + 1}`);
  
  // Run transcription via Python
  try {
    await spawnAndStream({
      jobId: job.id,
      label: `${AGENT_ALIASES.transcriber} (sub-chunk #${parentIdx + 1}.${subIdx + 1})`,
      command: PYTHON_BIN,
      args: ["-m", "cli.retranscribe_sub", "--config", retranscribeConfigPath],
      cwd: AGENT01_DIR,
      env: { OPENAI_API_KEY: apiKey },
      onStdout: (line) => {
        // Parse events from stdout
        tryProcessSplitEvent(job.id, parentIdx, line);
      },
    });
    
    // Mark as completed
    if (job.splitJobs[parentIdx] && job.splitJobs[parentIdx].subChunks[subIdx]) {
      job.splitJobs[parentIdx].subChunks[subIdx].status = "completed";
    }
    
    broadcastSplitEvent(job.id, {
      type: "split_progress",
      parentIdx,
      event: "sub_chunk_completed",
      subIdx,
    });
    
    pushLog(job.id, `[${AGENT_ALIASES.transcriber}] Sub-chunk #${parentIdx + 1}.${subIdx + 1} retranscribed successfully`);
    
  } catch (err) {
    if (job.splitJobs[parentIdx] && job.splitJobs[parentIdx].subChunks[subIdx]) {
      job.splitJobs[parentIdx].subChunks[subIdx].status = "failed";
    }
    
    broadcastSplitEvent(job.id, {
      type: "split_progress",
      parentIdx,
      event: "sub_chunk_failed",
      subIdx,
      error: err.message,
    });
    
    pushLog(job.id, `[${AGENT_ALIASES.transcriber}] Sub-chunk #${parentIdx + 1}.${subIdx + 1} retranscription failed: ${err.message}`, "error");
    throw err;
  }
}

/**
 * Skip a chunk entirely (mark as permanently skipped).
 */
export function skipChunk(job, chunkIdx) {
  // Remove from failed/cancelled arrays
  if (job.chunks) {
    job.chunks.failed = (job.chunks.failed || []).filter(i => i !== chunkIdx);
    job.chunks.cancelled = (job.chunks.cancelled || []).filter(i => i !== chunkIdx);
    
    // Add to a new "skipped" category or mark in completed with empty result
    if (!job.chunks.skipped) {
      job.chunks.skipped = [];
    }
    job.chunks.skipped.push(chunkIdx);
  }
  
  // Create a marker file
  const skipFlagPath = path.join(job.dir, "skip_signals", `skip_${chunkIdx}.flag`);
  fs.mkdirSync(path.dirname(skipFlagPath), { recursive: true });
  fs.writeFileSync(skipFlagPath, "skipped");
  
  pushLog(job.id, `[${AGENT_ALIASES.transcriber}] Chunk #${chunkIdx + 1} permanently skipped`);
  
  broadcast(job.id, {
    type: "chunk",
    payload: { status: "skipped", idx: chunkIdx },
  });
}

