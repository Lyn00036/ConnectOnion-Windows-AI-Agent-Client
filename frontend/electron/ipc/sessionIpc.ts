import { ipcMain } from 'electron'

import { sessionChannels } from './sessionChannels.ts'
import type { DesktopStorageService } from '../storage/service.ts'
import { getMeta, setMeta } from '../storage/agentRepository.ts'
import {
  bulkUpsertSessions,
  createSession,
  listSessions,
  removeSession,
  removeSessionsForAgent,
  updateSession,
  type SessionRecord,
  type SessionUpdate,
} from '../storage/sessionRepository.ts'

const SELECTED_SESSION_META_KEY = 'sessions.selectedId'

export function registerSessionIpc(storage: DesktopStorageService) {
  ipcMain.handle(sessionChannels.list, () => listSessions(storage.getDatabase()))

  ipcMain.handle(sessionChannels.create, (_event, session: SessionRecord) => {
    createSession(storage.getDatabase(), session)
  })

  ipcMain.handle(
    sessionChannels.update,
    (_event, id: string, fields: SessionUpdate) => {
      updateSession(storage.getDatabase(), id, fields)
    },
  )

  ipcMain.handle(sessionChannels.remove, (_event, id: string) => {
    removeSession(storage.getDatabase(), id)
  })

  ipcMain.handle(sessionChannels.removeForAgent, (_event, agentId: string) => {
    removeSessionsForAgent(storage.getDatabase(), agentId)
  })

  ipcMain.handle(sessionChannels.bulkUpsert, (_event, sessions: SessionRecord[]) => {
    bulkUpsertSessions(storage.getDatabase(), sessions)
  })

  ipcMain.handle(sessionChannels.getSelectedId, () => {
    const value = getMeta(storage.getDatabase(), SELECTED_SESSION_META_KEY)
    return value ? value : null
  })

  ipcMain.handle(sessionChannels.setSelectedId, (_event, id: string | null) => {
    setMeta(storage.getDatabase(), SELECTED_SESSION_META_KEY, id ?? '')
  })
}
