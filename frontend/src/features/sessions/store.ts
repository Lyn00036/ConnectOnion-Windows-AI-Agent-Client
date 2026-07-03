/** Zustand store for locally indexed conversations, grouped by saved agent. */

import { create } from 'zustand'

import {
  persistRemoveSession,
  persistRemoveSessionsForAgent,
  persistSelectedSessionId,
  persistSessionListChange,
  persistSessionTouch,
  readInitialSessions,
} from './sessionStorage'
import type { SessionSummary } from './types'

type SessionState = {
  sessions: SessionSummary[]
  selectedSessionId: string | null
  hydrated: boolean
  hydrate: () => Promise<void>
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

let hydrateStarted = false

export const useSessionStore = create<SessionState>((set, get) => ({
  sessions: [],
  selectedSessionId: null,
  hydrated: false,
  hydrate: async () => {
    // React StrictMode may mount effects twice in development; only read the
    // persisted session index once per renderer lifetime.
    if (hydrateStarted) return
    hydrateStarted = true
    try {
      const { sessions, selectedSessionId } = await readInitialSessions()
      set({ sessions, selectedSessionId, hydrated: true })
    } catch {
      set({ hydrated: true })
    }
  },
  ensureSession: (agentId) => {
    const { selectedSessionId, sessions } = get()
    const selectedSession = sessions.find(
      (session) => session.id === selectedSessionId && session.agentId === agentId,
    )
    if (selectedSession) return selectedSession.id

    // When switching agents, resume that agent's latest conversation instead
    // of creating a fresh one on every click.
    const recentSession = sessions
      .filter((session) => session.agentId === agentId)
      .sort((left, right) => right.updatedAt.localeCompare(left.updatedAt))[0]

    if (recentSession) {
      set({ selectedSessionId: recentSession.id })
      void persistSelectedSessionId(recentSession.id)
      return recentSession.id
    }

    const session = createSession(agentId, sessions)
    const nextSessions = [...sessions, session]
    set({ sessions: nextSessions, selectedSessionId: session.id })
    void persistSessionListChange(session, nextSessions)
    void persistSelectedSessionId(session.id)
    return session.id
  },
  setSelectedSessionId: (selectedSessionId) => {
    if (!get().sessions.some((session) => session.id === selectedSessionId)) return
    set({ selectedSessionId })
    void persistSelectedSessionId(selectedSessionId)
  },
  startNewSession: (agentId) => {
    const session = createSession(agentId, get().sessions)
    const sessions = [...get().sessions, session]
    set({ sessions, selectedSessionId: session.id })
    void persistSessionListChange(session, sessions)
    void persistSelectedSessionId(session.id)
    return session.id
  },
  touchSession: (sessionId, prompt) => {
    let touched: SessionSummary | undefined
    const sessions = get().sessions.map((session) => {
      if (session.id !== sessionId) return session

      // Replace the generic title with the first prompt once, then preserve any
      // explicit title the user may have created later.
      const canUsePromptAsTitle = /^Conversation \d+$/.test(session.title)
      const title =
        canUsePromptAsTitle && prompt?.trim()
          ? prompt.trim().replace(/\s+/g, ' ').slice(0, 48)
          : session.title

      touched = { ...session, title, updatedAt: new Date().toISOString() }
      return touched
    })

    if (!touched) return

    set({ sessions })
    void persistSessionTouch(touched, sessions)
  },
  removeSession: (sessionId) => {
    const sessions = get().sessions.filter((session) => session.id !== sessionId)
    const selectedSessionId =
      get().selectedSessionId === sessionId ? null : get().selectedSessionId
    set({ sessions, selectedSessionId })
    void persistRemoveSession(sessionId, sessions)
    void persistSelectedSessionId(selectedSessionId)
  },
  removeSessionsForAgent: (agentId) => {
    const removedIds = new Set(
      get().sessions.filter((session) => session.agentId === agentId).map((session) => session.id),
    )
    const sessions = get().sessions.filter((session) => session.agentId !== agentId)
    // If the active session belonged to the deleted agent, clear the pointer so
    // routing cannot reopen a conversation whose index entry is gone.
    const selectedSessionId =
      get().selectedSessionId && removedIds.has(get().selectedSessionId!)
        ? null
        : get().selectedSessionId
    set({ sessions, selectedSessionId })
    void persistRemoveSessionsForAgent(agentId, sessions)
    void persistSelectedSessionId(selectedSessionId)
  },
}))
