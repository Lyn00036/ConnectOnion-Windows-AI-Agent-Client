import { ipcMain } from 'electron'

import { agentChannels } from './agentChannels.ts'
import type { DesktopStorageService } from '../storage/service.ts'
import {
  bulkUpsertAgents,
  createAgent,
  getMeta,
  listAgents,
  removeAgent,
  setMeta,
  updateAgent,
  type AgentRecord,
  type AgentUpdate,
} from '../storage/agentRepository.ts'

const SELECTED_AGENT_META_KEY = 'agents.selectedId'

export function registerAgentIpc(storage: DesktopStorageService) {
  ipcMain.handle(agentChannels.list, () => listAgents(storage.getDatabase()))

  ipcMain.handle(agentChannels.create, (_event, agent: AgentRecord) => {
    createAgent(storage.getDatabase(), agent)
  })

  ipcMain.handle(
    agentChannels.update,
    (_event, id: string, fields: AgentUpdate) => {
      updateAgent(storage.getDatabase(), id, fields)
    },
  )

  ipcMain.handle(agentChannels.remove, (_event, id: string) => {
    removeAgent(storage.getDatabase(), id)
  })

  ipcMain.handle(agentChannels.bulkUpsert, (_event, agents: AgentRecord[]) => {
    bulkUpsertAgents(storage.getDatabase(), agents)
  })

  ipcMain.handle(agentChannels.getSelectedId, () => {
    const value = getMeta(storage.getDatabase(), SELECTED_AGENT_META_KEY)
    return value ? value : null
  })

  ipcMain.handle(agentChannels.setSelectedId, (_event, id: string | null) => {
    setMeta(storage.getDatabase(), SELECTED_AGENT_META_KEY, id ?? '')
  })
}
