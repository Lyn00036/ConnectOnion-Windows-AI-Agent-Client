/**
 * Session persistence adapter (SQLite via preload, localStorage fallback).
 */

import type { DesktopSessionRecord } from '../../shared/storage/sessions'
import { withDesktopFallback } from '../../shared/storage/desktopFallback'
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
  try {
    localStorage.setItem(SESSIONS_KEY, JSON.stringify(sessions))
  } catch {
    // Keep in-memory state working if storage is unavailable.
  }
}

function saveSelectedIdToLocalStorage(selectedSessionId: string | null) {
  try {
    if (selectedSessionId) {
      localStorage.setItem(ACTIVE_SESSION_KEY, selectedSessionId)
    } else {
      localStorage.removeItem(ACTIVE_SESSION_KEY)
    }
  } catch {
    // Keep in-memory state working if storage is unavailable.
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

async function readFromDesktop(
  desktop: NonNullable<ReturnType<typeof getDesktopSessions>>,
): Promise<{ sessions: SessionSummary[]; selectedSessionId: string | null }> {
  await migrateLegacySessionsOnce(desktop)
  const records = await desktop.list()
  const sessions = records.map(toSummary)
  const storedSelectedId = await desktop.getSelectedId()
  const selectedSessionId = sessions.some((session) => session.id === storedSelectedId)
    ? storedSelectedId
    : null
  return { sessions, selectedSessionId }
}

export type InitialSessionsData = {
  sessions: SessionSummary[]
  selectedSessionId: string | null
}

export async function readInitialSessions(): Promise<InitialSessionsData> {
  const desktop = getDesktopSessions()
  const sessions = loadSessionsFromLocalStorage()

  return withDesktopFallback(
    Boolean(desktop),
    () => readFromDesktop(desktop!),
    () => ({ sessions, selectedSessionId: loadSelectedIdFromLocalStorage(sessions) }),
  )
}

export async function persistRemoveSession(sessionId: string, sessionsAfter: SessionSummary[]) {
  const desktop = getDesktopSessions()
  await withDesktopFallback(
    Boolean(desktop),
    () => desktop!.remove(sessionId).then(() => undefined),
    () => saveSessionsToLocalStorage(sessionsAfter),
  )
}

export async function persistRemoveSessionsForAgent(
  agentId: string,
  sessionsAfter: SessionSummary[],
) {
  const desktop = getDesktopSessions()
  await withDesktopFallback(
    Boolean(desktop),
    () => desktop!.removeForAgent(agentId).then(() => undefined),
    () => saveSessionsToLocalStorage(sessionsAfter),
  )
}

export async function persistSelectedSessionId(selectedSessionId: string | null) {
  const desktop = getDesktopSessions()
  await withDesktopFallback(
    Boolean(desktop),
    () => desktop!.setSelectedId(selectedSessionId).then(() => undefined),
    () => saveSelectedIdToLocalStorage(selectedSessionId),
  )
}

export async function persistSessionListChange(
  session: SessionSummary | null,
  sessionsAfter: SessionSummary[],
) {
  const desktop = getDesktopSessions()
  await withDesktopFallback(
    Boolean(desktop),
    () => {
      if (!session) return Promise.resolve()
      return desktop!.create(toRecord(session)).then(() => undefined)
    },
    () => saveSessionsToLocalStorage(sessionsAfter),
  )
}

export async function persistSessionTouch(session: SessionSummary, sessionsAfter: SessionSummary[]) {
  const desktop = getDesktopSessions()
  await withDesktopFallback(
    Boolean(desktop),
    () =>
      desktop!.update(session.id, {
        title: session.title,
        updatedAt: session.updatedAt,
      }).then(() => undefined),
    () => saveSessionsToLocalStorage(sessionsAfter),
  )
}
