import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';
import path from 'path';

// The API the dev server sends /api and /health to; XTRACT_API names another one (a second copy on another port).
const api = process.env.XTRACT_API || 'http://localhost:5010';

export default defineConfig({
  plugins: [react()],
  resolve: {
    alias: { '@': path.resolve(__dirname, './src') },
  },
  server: {
    port: 5173,
    proxy: {
      '/api': { target: api, changeOrigin: true },
      '/health': { target: api, changeOrigin: true },
    },
  },
});
