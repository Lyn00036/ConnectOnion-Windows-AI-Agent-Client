import { app, BrowserWindow } from 'electron'
import { dirname, join } from 'node:path'
import { fileURLToPath } from 'node:url'

import { registerStorageIpc } from './ipc/storageIpc.ts'
import { createDesktopStorage } from './storage/service.ts'

app.setName('connectonion_desktop')

const __filename = fileURLToPath(import.meta.url)
const __dirname = dirname(__filename)

const storage = createDesktopStorage(() => app.getPath('userData'))

function createWindow() {
  const win = new BrowserWindow({
    width: 1200,
    height: 800,
    icon: join(__dirname, '../src/assets/icon.ico'),
    webPreferences: {
      contextIsolation: true,
      nodeIntegration: false,
      preload: join(__dirname, 'preload.ts'),
      sandbox: false,
    },
  })

  win.loadURL('http://localhost:3000')

  win.webContents.openDevTools()
}

app.whenReady().then(() => {
  storage.initialize()
  registerStorageIpc(storage)
  createWindow()
})

app.on('window-all-closed', () => {
  if (process.platform !== 'darwin') {
    app.quit()
  }
})

app.on('before-quit', () => {
  storage.close()
})
