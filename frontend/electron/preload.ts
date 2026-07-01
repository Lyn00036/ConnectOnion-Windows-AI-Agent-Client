import type { IpcRendererEvent } from 'electron'
import { contextBridge, ipcRenderer } from 'electron'

import { windowChannels } from './ipc/windowChannels.ts'
import type { DesktopWindowApi, DesktopWindowState } from '../src/shared/desktopWindow.ts'

const desktopWindow: DesktopWindowApi = {
  close: () => ipcRenderer.invoke(windowChannels.close),
  getState: () => ipcRenderer.invoke(windowChannels.getState),
  minimize: () => ipcRenderer.invoke(windowChannels.minimize),
  onStateChange: (handler: (state: DesktopWindowState) => void) => {
    const listener = (_event: IpcRendererEvent, state: DesktopWindowState) => {
      handler(state)
    }

    ipcRenderer.on(windowChannels.stateChanged, listener)

    return () => {
      ipcRenderer.removeListener(windowChannels.stateChanged, listener)
    }
  },
  toggleMaximize: () => ipcRenderer.invoke(windowChannels.toggleMaximize),
}

contextBridge.exposeInMainWorld('desktopWindow', desktopWindow)
