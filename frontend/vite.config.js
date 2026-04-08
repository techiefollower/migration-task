import path from 'path'
import { fileURLToPath } from 'url'
import { defineConfig, loadEnv } from 'vite'
import react from '@vitejs/plugin-react'

const __dirname = path.dirname(fileURLToPath(import.meta.url))

// https://vite.dev/config/
export default defineConfig(({ mode }) => {
  // Always load .env* from the folder that contains this file (not process.cwd() when you run from repo root).
  const env = loadEnv(mode, __dirname, 'VITE_')
  const proxyTarget =
    env.VITE_API_PROXY_TARGET?.trim() || 'http://127.0.0.1:5096'

  return {
    plugins: [react()],
    server: {
      port: 5173,
      proxy: {
        '/api': {
          target: proxyTarget,
          changeOrigin: true,
          configure: (proxy) => {
            proxy.on('error', (err) => {
              console.error('[vite proxy /api]', err.message)
            })
            proxy.on('proxyReq', (proxyReq) => proxyReq.setTimeout(600_000))
            proxy.on('proxyRes', (proxyRes) => proxyRes.setTimeout(600_000))
          },
        },
      },
    },
  }
})
