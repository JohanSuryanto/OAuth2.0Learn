/// <reference types="vitest/config" />
import react from '@vitejs/plugin-react'
import { defineConfig } from 'vite'

// https://vite.dev/config/
export default defineConfig({
  plugins: [react()],
  server: {
    port: 5174,
    strictPort: true,
    headers: {
      'X-Frame-Options': 'DENY',
      'X-Content-Type-Options': 'nosniff',
      'Referrer-Policy': 'no-referrer',
    },
    proxy: {
      // All API calls go through this proxy so the browser sees them as same-origin
      // and the auth cookie is sent with them (research R3).
      '/api': {
        target: 'https://localhost:5001',
        // DEV ONLY: skips TLS verification of the local ASP.NET Core dev certificate.
        secure: false,
        changeOrigin: false,
      },
    },
  },
  test: {
    environment: 'jsdom',
    globals: true,
    setupFiles: './src/setupTests.ts',
  },
})
