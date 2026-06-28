import { defineConfig } from 'vitest/config'
import react from '@vitejs/plugin-react'
import tailwindcss from '@tailwindcss/vite'
import { fileURLToPath, URL } from 'node:url'

// https://vite.dev/config/
export default defineConfig({
  plugins: [
    react(),
    tailwindcss(),
  ],
  test: {
    environment: 'jsdom',
    include: ['src/**/*.{test,spec}.{ts,tsx}'],
    setupFiles: './src/test/setup.ts',
  },
  resolve: {
    alias: [
      {
        find: '@',
        replacement: fileURLToPath(new URL('./src', import.meta.url)),
      },
      {
        find: 'connectonion-connect',
        replacement: fileURLToPath(
          new URL(
            './node_modules/connectonion/dist/connect/index.js',
            import.meta.url,
          ),
        ),
      },
      {
        find: '../address',
        replacement: fileURLToPath(
          new URL(
            './node_modules/connectonion/dist/address-browser.js',
            import.meta.url,
          ),
        ),
      },
    ],
  },
})
