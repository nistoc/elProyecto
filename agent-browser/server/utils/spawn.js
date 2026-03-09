import { spawn } from "child_process";
import { pushLog } from "../services/broadcaster.js";
import { tryProcessChunkEvent } from "../services/chunkState.js";

/**
 * Spawn a process and stream its output as logs.
 * 
 * @param {Object} options
 * @param {string} options.jobId - Job ID for logging
 * @param {string} options.label - Label for log prefix
 * @param {string} options.command - Command to run
 * @param {string[]} options.args - Command arguments
 * @param {string} options.cwd - Working directory
 * @param {Object} options.env - Additional environment variables
 * @param {Function} [options.onStdout] - Optional callback for each stdout line
 */
export function spawnAndStream({
  jobId,
  label,
  command,
  args,
  cwd,
  env = {},
  onStdout,
}) {
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
        
        // Call custom stdout handler if provided
        if (onStdout && level === "info") {
          onStdout(line);
        }
        
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

