import { app, BrowserWindow, Menu } from 'electron'
import { dirname, join } from 'node:path'
import { fileURLToPath } from 'node:url'

import { registerWindowIpc } from './ipc/windowIpc.ts'

app.setName('connectonion_desktop')

const __filename = fileURLToPath(import.meta.url)
const __dirname = dirname(__filename)

function createWindow() {
  const win = new BrowserWindow({
    autoHideMenuBar: true,
    backgroundColor: '#fbfbfc',
    frame: false,
    width: 1200,
    height: 800,
    icon: join(__dirname, '../src/assets/icon.ico'),
    minHeight: 640,
    minWidth: 960,
    webPreferences: {
      contextIsolation: true,
      nodeIntegration: false,
      preload: join(__dirname, 'preload.ts'),
      sandbox: false,
    },
  })

  registerWindowIpc(win)
  win.setMenuBarVisibility(false)
  win.loadURL('http://localhost:3000')
}

app.whenReady().then(() => {
  Menu.setApplicationMenu(null)
  createWindow()
})

app.on('window-all-closed', () => {
  if (process.platform !== 'darwin') {
    app.quit()
  }
})

