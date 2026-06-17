import { contextBridge, ipcRenderer } from 'electron'

import { storageChannels } from './ipc/storageChannels.ts'
import type { DesktopStorageApi } from '../src/shared/storage/types.ts'

// TODO(preload-api): Add narrow, typed APIs here as features move to SQLite.
// Avoid exposing raw ipcRenderer, file system access, or SQL to the renderer.
// Expected future bridges:
// - desktopAgents
// - desktopSessions
// - desktopMessages
// - desktopFiles
// - desktopCredentials

const desktopStorage: DesktopStorageApi = {
  getStatus: () => ipcRenderer.invoke(storageChannels.getStatus),
}

contextBridge.exposeInMainWorld('desktopStorage', desktopStorage)
