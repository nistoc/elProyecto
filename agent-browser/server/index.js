import express from "express";
import fs from "fs";
import path from "path";
import { fileURLToPath } from "url";
import dotenv from "dotenv";
import { PORT, RUNTIME_DIR, UPLOADS_DIR } from "./config.js";
import jobsRouter from "./routes/jobs.js";

// Load environment variables from .env files
const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);

// Try to load .env from multiple locations
const envPaths = [
  path.resolve(__dirname, "..", ".env"),           // agent-browser/.env
  path.resolve(__dirname, "..", "..", ".env"),     // elProyecto/.env
  path.resolve(__dirname, "..", "..", "agent01", ".env"), // agent01/.env
];

for (const envPath of envPaths) {
  if (fs.existsSync(envPath)) {
    dotenv.config({ path: envPath });
    break;
  }
}

const app = express();

// CORS middleware for local dev (Vite runs on 5173)
app.use((req, res, next) => {
  res.setHeader("Access-Control-Allow-Origin", "*");
  res.setHeader("Access-Control-Allow-Headers", "Content-Type");
  res.setHeader("Access-Control-Allow-Methods", "GET,POST,PUT,DELETE,OPTIONS");
  if (req.method === "OPTIONS") {
    res.sendStatus(204);
    return;
  }
  next();
});

app.use(express.json());

// Ensure runtime directories exist
fs.mkdirSync(RUNTIME_DIR, { recursive: true });
fs.mkdirSync(UPLOADS_DIR, { recursive: true });

// Static files for runtime artifacts
app.use("/runtime", express.static(RUNTIME_DIR));

// API routes
app.use("/api/jobs", jobsRouter);

// Health check
app.get("/health", (req, res) => {
  res.json({ status: "ok" });
});

app.listen(PORT, () => {
  console.log(`API server listening on http://localhost:${PORT}`);
});
