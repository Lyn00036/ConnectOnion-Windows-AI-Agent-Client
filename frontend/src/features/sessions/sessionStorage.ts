/**
 * Session persistence adapter (SQLite via preload, localStorage fallback).
 */

import type { DesktopSessionRecord } from '../../shared/storage/sessions'
import type { SessionSummary } from './types'

const SESSIONS_KEY = 'connectonion.sessions'
const ACTIVE_SESSION_KEY = 'connectonion.activeSessionId'
const MIGRATION_FLAG_KEY = 'connectonion.sqliteMigrated.sessions'

function getDesktopSessions() {
  return window.desktopSessions ?? null
}

function toRecord(session: SessionSummary): DesktopSessionRecord {
  return {
    id: session.id,
    agentId: session.agentId,
    title: session.title,
    createdAt: session.createdAt,
    updatedAt: session.updatedAt,
  }
}

function toSummary(record: DesktopSessionRecord): SessionSummary {
  return {
    id: record.id,
    agentId: record.agentId,
    title: record.title,
    createdAt: record.createdAt,
    updatedAt: record.updatedAt,
  }
}

function loadSessionsFromLocalStorage(): SessionSummary[] {
  try {
    const stored = localStorage.getItem(SESSIONS_KEY)
    if (!stored) return []

    const sessions = JSON.parse(stored) as SessionSummary[]
    return Array.isArray(sessions)
      ? sessions.filter((session) => session.id && session.agentId && session.title)
      : []
  } catch {
    return []
  }
}

function loadSelectedIdFromLocalStorage(sessions: SessionSummary[]): string | null {
  const stored = localStorage.getItem(ACTIVE_SESSION_KEY)
  return sessions.some((session) => session.id === stored) ? stored! : null
}

function saveSessionsToLocalStorage(sessions: SessionSummary[]) {
  localStorage.setItem(SESSIONS_KEY, JSON.stringify(sessions))
}

function saveSelectedIdToLocalStorage(selectedSessionId: string | null) {
  if (selectedSessionId) {
    localStorage.setItem(ACTIVE_SESSION_KEY, selectedSessionId)
  } else {
    localStorage.removeItem(ACTIVE_SESSION_KEY)
  }
}

async function migrateLegacySessionsOnce(
  desktop: NonNullable<ReturnType<typeof getDesktopSessions>>,
) {
  if (localStorage.getItem(MIGRATION_FLAG_KEY)) return

  const legacySessions = loadSessionsFromLocalStorage()
  if (legacySessions.length > 0) {
    await desktop.bulkUpsert(legacySessions.map(toRecord))
    const legacySelected = loadSelectedIdFromLocalStorage(legacySessions)
    if (legacySelected) await desktop.setSelectedId(legacySelected)
  }

  localStorage.setItem(MIGRATION_FLAG_KEY, '1')
}

export type InitialSessionsData = {
  sessions: SessionSummary[]
  selectedSessionId: string | null
}

export async function readInitialSessions(): Promise<InitialSessionsData> {
  const desktop = getDesktopSessions()

  if (!desktop) {
    const sessions = loadSessionsFromLocalStorage()
    return { sessions, selectedSessionId: loadSelectedIdFromLocalStorage(sessions) }
  }

  await migrateLegacySessionsOnce(desktop)

  const records = await desktop.list()
  const sessions = records.map(toSummary)
  const storedSelectedId = await desktop.getSelectedId()
  const selectedSessionId = sessions.some((session) => session.id === storedSelectedId)
    ? storedSelectedId
    : null

  return { sessions, selectedSessionId }
}

export async function persistCreateSession(session: SessionSummary) {
  const desktop = getDesktopSessions()
  if (desktop) {
    await desktop.create(toRecord(session))
    return
  }
}

export async function persistUpdateSession(session: SessionSummary) {
  const desktop = getDesktopSessions()
  if (desktop) {
    await desktop.update(session.id, {
      title: session.title,
      updatedAt: session.updatedAt,
    })
    return
  }
}

export async function persistTouchSessions(sessions: SessionSummary[]) {
  const desktop = getDesktopSessions()
  if (desktop) {
    await Promise.all(
      sessions.map((session) =>
        desktop.update(session.id, {
          title: session.title,
          updatedAt: session.updatedAt,
        }),
      ),
    )
    return
  }
  saveSessionsToLocalStorage(sessions)
}

export async function persistAllSessions(sessions: SessionSummary[]) {
  const desktop = getDesktopSessions()
  if (desktop) {
    // Desktop path persists individual ops; bulk replace is only for migration.
    return
  }
  saveSessionsToLocalStorage(sessions)
}

export async function persistRemoveSession(sessionId: string, sessionsAfter: SessionSummary[]) {
  const desktop = getDesktopSessions()
  if (desktop) {
    await desktop.remove(sessionId)
    return
  }
  saveSessionsToLocalStorage(sessionsAfter)
}

export async function persistRemoveSessionsForAgent(
  agentId: string,
  sessionsAfter: SessionSummary[],
) {
  const desktop = getDesktopSessions()
  if (desktop) {
    await desktop.removeForAgent(agentId)
    return
  }
  saveSessionsToLocalStorage(sessionsAfter)
}

export async function persistSelectedSessionId(selectedSessionId: string | null) {
  const desktop = getDesktopSessions()
  if (desktop) {
    await desktop.setSelectedId(selectedSessionId)
    return
  }
  saveSelectedIdToLocalStorage(selectedSessionId)
}

/** Persist a newly created session and the full list (localStorage fallback). */
export async function persistSessionListChange(
  session: SessionSummary | null,
  sessionsAfter: SessionSummary[],
) {
  const desktop = getDesktopSessions()
  if (desktop) {
    if (session) await desktop.create(toRecord(session))
    return
  }
  saveSessionsToLocalStorage(sessionsAfter)
}

/** Persist title/timestamp updates for one session. */
export async function persistSessionTouch(session: SessionSummary, sessionsAfter: SessionSummary[]) {
  const desktop = getDesktopSessions()
  if (desktop) {
    await desktop.update(session.id, {
      title: session.title,
      updatedAt: session.updatedAt,
    })
    return
  }
  saveSessionsToLocalStorage(sessionsAfter)
}
