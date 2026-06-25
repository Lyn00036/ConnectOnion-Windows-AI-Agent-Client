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
