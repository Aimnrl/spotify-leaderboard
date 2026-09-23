import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

// Dev: open http://127.0.0.1:5173. /api and /auth go to the .NET API, so the session
// cookie is same-origin and no CORS is needed. Spotify requires 127.0.0.1, not localhost.
export default defineConfig({
  plugins: [react()],
  server: {
    host: '127.0.0.1',
    port: 5173,
    strictPort: true,
    proxy: {
      '/api': 'http://127.0.0.1:5000',
      '/auth': 'http://127.0.0.1:5000',
    },
  },
  // Prod: the API serves the built app from wwwroot.
  build: { outDir: '../src/Api/wwwroot', emptyOutDir: true },
})
