import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import tailwindcss from '@tailwindcss/vite'
import { fileURLToPath, URL } from 'node:url'

// https://vite.dev/config/
export default defineConfig({
  plugins: [
    react(),
    tailwindcss(),
  ],
  resolve: {
    alias: [
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
