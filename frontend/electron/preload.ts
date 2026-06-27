import type { IpcRendererEvent } from 'electron'
import { contextBridge, ipcRenderer } from 'electron'

import { agentChannels } from './ipc/agentChannels.ts'
import { storageChannels } from './ipc/storageChannels.ts'
import { windowChannels } from './ipc/windowChannels.ts'
import type { DesktopWindowApi, DesktopWindowState } from '../src/shared/desktopWindow.ts'
import type {
  DesktopAgentRecord,
  DesktopAgentUpdate,
  DesktopAgentsApi,
} from '../src/shared/storage/agents.ts'
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

const desktopAgents: DesktopAgentsApi = {
  list: () => ipcRenderer.invoke(agentChannels.list),
  create: (agent: DesktopAgentRecord) =>
    ipcRenderer.invoke(agentChannels.create, agent),
  update: (id: string, fields: DesktopAgentUpdate) =>
    ipcRenderer.invoke(agentChannels.update, id, fields),
  remove: (id: string) => ipcRenderer.invoke(agentChannels.remove, id),
  bulkUpsert: (agents: DesktopAgentRecord[]) =>
    ipcRenderer.invoke(agentChannels.bulkUpsert, agents),
  getSelectedId: () => ipcRenderer.invoke(agentChannels.getSelectedId),
  setSelectedId: (id: string | null) =>
    ipcRenderer.invoke(agentChannels.setSelectedId, id),
}

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

contextBridge.exposeInMainWorld('desktopStorage', desktopStorage)
contextBridge.exposeInMainWorld('desktopAgents', desktopAgents)
contextBridge.exposeInMainWorld('desktopWindow', desktopWindow)
