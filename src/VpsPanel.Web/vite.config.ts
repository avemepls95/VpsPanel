import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

export default defineConfig({
  plugins: [react()],
  build: {
    outDir: '../VpsPanel/wwwroot',
    emptyOutDir: true,
  },
  server: {
    port: 5173,
    proxy: {
      // changeOrigin sets the backend Host, otherwise the Host check returns 400.
      '/api': { target: 'http://127.0.0.1:5199', changeOrigin: true },
    },
  },
});
