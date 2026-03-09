import path from "path";
import { fileURLToPath } from "url";

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);

export const ROOT = path.resolve(__dirname, "..");
export const AGENT01_DIR = path.resolve(ROOT, "..", "agent01");
export const AGENT03_DIR = path.resolve(ROOT, "..", "agent03-trans-improver");
export const RUNTIME_DIR = path.join(ROOT, "runtime");
export const UPLOADS_DIR = path.join(RUNTIME_DIR, "uploads");

export const PORT = process.env.PORT || 3001;
export const PYTHON_BIN = process.env.PYTHON_BIN || "python";

export const AGENT_ALIASES = {
  transcriber: "Transcriber Agent",
  refiner: "Refiner Agent",
};

