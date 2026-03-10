import { spawn, spawnSync } from "child_process";
import { fileURLToPath } from "url";
import path from "path";

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const root = path.resolve(__dirname, "..");

function parseArg(name, short) {
  const i = process.argv.indexOf(name);
  if (i !== -1 && process.argv[i + 1]) return process.argv[i + 1];
  const j = process.argv.indexOf(short);
  if (j !== -1 && process.argv[j + 1]) return process.argv[j + 1];
  return null;
}

const apiPort = parseArg("--api-port", "-a");
const frontPort = parseArg("--front-port", "-f");

if (apiPort) {
  process.env.PORT = String(apiPort);
  process.env.VITE_API_BASE = `http://localhost:${apiPort}`;
}
if (frontPort) process.env.VITE_DEV_PORT = String(frontPort);

// Убедиться, что зависимости установлены (один вызов, без лишнего шума при уже установленном)
const install = spawnSync("npm", ["install"], {
  stdio: "inherit",
  cwd: root,
  shell: true,
  env: process.env,
});
if (install.error) {
  console.error("npm install failed:", install.error.message);
  process.exit(1);
}
if (install.status !== 0) {
  process.exit(install.status ?? 1);
}

// Одна строка для shell — корректная работа на Windows и отсутствие DEP0190
const runCmd = "npx concurrently \"npm run dev\" \"npm run dev:server\"";
const child = spawn(runCmd, [], {
  stdio: "inherit",
  env: process.env,
  shell: true,
  cwd: root,
});
child.on("exit", (code) => process.exit(code ?? 0));
