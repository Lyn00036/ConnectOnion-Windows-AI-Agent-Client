/** Zustand store for locally indexed conversations, grouped by saved agent. */

import { create } from 'zustand'

import type { SessionSummary } from './types'

const SESSIONS_KEY = 'connectonion.sessions'
const ACTIVE_SESSION_KEY = 'connectonion.activeSessionId'

type SessionState = {
  sessions: SessionSummary[]
  selectedSessionId: string | null
  ensureSession: (agentId: string) => string
  setSelectedSessionId: (sessionId: string) => void
  startNewSession: (agentId: string) => string
  touchSession: (sessionId: string, prompt?: string) => void
  removeSession: (sessionId: string) => void
  removeSessionsForAgent: (agentId: string) => void
}

function createSessionId() {
  return crypto.randomUUID()
}

function loadSessions() {
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

function saveSessions(sessions: SessionSummary[]) {
  localStorage.setItem(SESSIONS_KEY, JSON.stringify(sessions))
}

function createSession(agentId: string, existingSessions: SessionSummary[]) {
  const timestamp = new Date().toISOString()
  const conversationNumber =
    existingSessions.filter((session) => session.agentId === agentId).length + 1

  return {
    id: createSessionId(),
    agentId,
    title: `Conversation ${conversationNumber}`,
    createdAt: timestamp,
    updatedAt: timestamp,
  }
}

const initialSessions = loadSessions()
const storedSelectedSessionId = localStorage.getItem(ACTIVE_SESSION_KEY)
const initialSelectedSessionId = initialSessions.some(
  (session) => session.id === storedSelectedSessionId,
)
  ? storedSelectedSessionId
  : null

export const useSessionStore = create<SessionState>((set, get) => ({
  sessions: initialSessions,
  selectedSessionId: initialSelectedSessionId,
  ensureSession: (agentId) => {
    const { selectedSessionId, sessions } = get()
    const selectedSession = sessions.find(
      (session) => session.id === selectedSessionId && session.agentId === agentId,
    )
    if (selectedSession) return selectedSession.id

    const recentSession = sessions
      .filter((session) => session.agentId === agentId)
      .sort((left, right) => right.updatedAt.localeCompare(left.updatedAt))[0]

    if (recentSession) {
      localStorage.setItem(ACTIVE_SESSION_KEY, recentSession.id)
      set({ selectedSessionId: recentSession.id })
      return recentSession.id
    }

    const session = createSession(agentId, sessions)
    const nextSessions = [...sessions, session]
    saveSessions(nextSessions)
    localStorage.setItem(ACTIVE_SESSION_KEY, session.id)
    set({ sessions: nextSessions, selectedSessionId: session.id })
    return session.id
  },
  setSelectedSessionId: (selectedSessionId) => {
    if (!get().sessions.some((session) => session.id === selectedSessionId)) return
    localStorage.setItem(ACTIVE_SESSION_KEY, selectedSessionId)
    set({ selectedSessionId })
  },
  startNewSession: (agentId) => {
    const session = createSession(agentId, get().sessions)
    const sessions = [...get().sessions, session]
    saveSessions(sessions)
    localStorage.setItem(ACTIVE_SESSION_KEY, session.id)
    set({ sessions, selectedSessionId: session.id })
    return session.id
  },
  touchSession: (sessionId, prompt) => {
    const sessions = get().sessions.map((session) => {
      if (session.id !== sessionId) return session

      const canUsePromptAsTitle = /^Conversation \d+$/.test(session.title)
      const title =
        canUsePromptAsTitle && prompt?.trim()
          ? prompt.trim().replace(/\s+/g, ' ').slice(0, 48)
          : session.title

      return { ...session, title, updatedAt: new Date().toISOString() }
    })
    saveSessions(sessions)
    set({ sessions })
  },
  removeSession: (sessionId) => {
    const sessions = get().sessions.filter((session) => session.id !== sessionId)
    const selectedSessionId =
      get().selectedSessionId === sessionId ? null : get().selectedSessionId
    saveSessions(sessions)
    if (selectedSessionId) {
      localStorage.setItem(ACTIVE_SESSION_KEY, selectedSessionId)
    } else {
      localStorage.removeItem(ACTIVE_SESSION_KEY)
    }
    set({ sessions, selectedSessionId })
  },
  removeSessionsForAgent: (agentId) => {
    const removedIds = new Set(
      get().sessions.filter((session) => session.agentId === agentId).map((session) => session.id),
    )
    const sessions = get().sessions.filter((session) => session.agentId !== agentId)
    const selectedSessionId =
      get().selectedSessionId && removedIds.has(get().selectedSessionId!)
        ? null
        : get().selectedSessionId
    saveSessions(sessions)
    if (selectedSessionId) {
      localStorage.setItem(ACTIVE_SESSION_KEY, selectedSessionId)
    } else {
      localStorage.removeItem(ACTIVE_SESSION_KEY)
    }
    set({ sessions, selectedSessionId })
  },
}))
