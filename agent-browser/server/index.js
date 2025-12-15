import express from "express";
import fs from "fs";
import { PORT, RUNTIME_DIR, UPLOADS_DIR } from "./config.js";
import jobsRouter from "./routes/jobs.js";

const app = express();

// CORS middleware for local dev (Vite runs on 5173)
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
