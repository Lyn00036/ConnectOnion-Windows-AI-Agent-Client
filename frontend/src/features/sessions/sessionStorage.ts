/**
 * Session persistence adapter backed by localStorage.
 */

import type { SessionSummary } from './types'

const SESSIONS_KEY = 'connectonion.sessions'
const ACTIVE_SESSION_KEY = 'connectonion.activeSessionId'

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

export type InitialSessionsData = {
  sessions: SessionSummary[]
  selectedSessionId: string | null
}

export async function readInitialSessions(): Promise<InitialSessionsData> {
  const sessions = loadSessionsFromLocalStorage()

  return { sessions, selectedSessionId: loadSelectedIdFromLocalStorage(sessions) }
}

export async function persistRemoveSession(
  _sessionId: string,
  sessionsAfter: SessionSummary[],
) {
  saveSessionsToLocalStorage(sessionsAfter)
}

export async function persistRemoveSessionsForAgent(
  _agentId: string,
  sessionsAfter: SessionSummary[],
) {
  saveSessionsToLocalStorage(sessionsAfter)
}

export async function persistSelectedSessionId(selectedSessionId: string | null) {
  saveSelectedIdToLocalStorage(selectedSessionId)
}

export async function persistSessionListChange(
  _session: SessionSummary | null,
  sessionsAfter: SessionSummary[],
) {
  saveSessionsToLocalStorage(sessionsAfter)
}

export async function persistSessionTouch(
  _session: SessionSummary,
  sessionsAfter: SessionSummary[],
) {
  saveSessionsToLocalStorage(sessionsAfter)
}
