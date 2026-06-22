/** Zustand store for the active SDK session id. */

import { create } from 'zustand'

const ACTIVE_SESSION_KEY = 'connectonion.activeSessionId'

type SessionState = {
  selectedSessionId: string
  setSelectedSessionId: (sessionId: string) => void
  startNewSession: () => void
}

function createSessionId() {
  return crypto.randomUUID()
}

function loadSessionId() {
  const storedSessionId = localStorage.getItem(ACTIVE_SESSION_KEY)
  if (storedSessionId) return storedSessionId

  const sessionId = createSessionId()
  localStorage.setItem(ACTIVE_SESSION_KEY, sessionId)
  return sessionId
}

export const useSessionStore = create<SessionState>((set) => ({
  selectedSessionId: loadSessionId(),
  setSelectedSessionId: (selectedSessionId) => {
    localStorage.setItem(ACTIVE_SESSION_KEY, selectedSessionId)
    set({ selectedSessionId })
  },
  startNewSession: () => {
    const selectedSessionId = createSessionId()
    localStorage.setItem(ACTIVE_SESSION_KEY, selectedSessionId)
    set({ selectedSessionId })
  },
}))
