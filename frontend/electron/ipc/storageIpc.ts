import { ipcMain } from 'electron'

import { storageChannels } from './storageChannels.ts'
import type { DesktopStorageService } from '../storage/service.ts'

// TODO(storage-ipc): Keep this module for storage health and schema metadata only.
// Feature CRUD should live in feature-specific IPC modules such as agentIpc,
// sessionIpc, messageIpc, fileIpc, and credentialIpc.

export function registerStorageIpc(storage: DesktopStorageService) {
  ipcMain.handle(storageChannels.getStatus, () => storage.getStatus())
}
