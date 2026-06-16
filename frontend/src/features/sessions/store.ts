/** Zustand store reserved for the selected session and session sidebar state. */

import { create } from 'zustand'

type SessionState = {
  selectedSessionId: string | null
  setSelectedSessionId: (sessionId: string | null) => void
}

export const useSessionStore = create<SessionState>((set) => ({
  selectedSessionId: null,
  setSelectedSessionId: (selectedSessionId) => set({ selectedSessionId }),
}))
