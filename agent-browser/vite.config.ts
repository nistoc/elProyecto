import { defineConfig } from "vite";

const devPort = process.env.VITE_DEV_PORT ? Number(process.env.VITE_DEV_PORT) : 5173;

export default defineConfig({
  server: {
    port: devPort,
  },
});
