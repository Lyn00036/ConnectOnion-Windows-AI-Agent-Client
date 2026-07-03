import type { BrowserWindow, IpcMainInvokeEvent } from 'electron'
import { BrowserWindow as ElectronBrowserWindow, ipcMain } from 'electron'

import type { DesktopWindowState } from '../../src/shared/desktopWindow.ts'
import { windowChannels } from './windowChannels.ts'

function getDesktopWindowState(window: BrowserWindow): DesktopWindowState {
  return {
    isMaximized: window.isMaximized(),
  }
}

function getSenderWindow(event: IpcMainInvokeEvent, fallbackWindow: BrowserWindow) {
  // Resolve the real window for the IPC sender so this module still works if
  // another desktop window is added later.
  return ElectronBrowserWindow.fromWebContents(event.sender) ?? fallbackWindow
}

export function registerWindowIpc(window: BrowserWindow) {
  const sendState = () => {
    if (!window.isDestroyed()) {
      window.webContents.send(windowChannels.stateChanged, getDesktopWindowState(window))
    }
  }

  ipcMain.handle(windowChannels.getState, (event) => {
    return getDesktopWindowState(getSenderWindow(event, window))
  })

  ipcMain.handle(windowChannels.minimize, (event) => {
    getSenderWindow(event, window).minimize()
  })

  ipcMain.handle(windowChannels.toggleMaximize, (event) => {
    const senderWindow = getSenderWindow(event, window)
    // Return the fresh state so the renderer can update its maximize icon
    // immediately, before the async maximize/unmaximize event arrives.
    if (senderWindow.isMaximized()) {
      senderWindow.unmaximize()
    } else {
      senderWindow.maximize()
    }
    return getDesktopWindowState(senderWindow)
  })

  ipcMain.handle(windowChannels.close, (event) => {
    getSenderWindow(event, window).close()
  })

  window.on('maximize', sendState)
  window.on('unmaximize', sendState)
}
