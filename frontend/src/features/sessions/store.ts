/** Zustand store reserved for the selected session and session sidebar state. */

// TODO(session-storage): Back this store with SQLite through Electron IPC.
// Required follow-up:
// - Load recent sessions ordered by updated_at.
// - Create a new session when the user starts a new chat.
// - Persist selected session id and restore it on launch.
// - Support rename/delete/archive if the UI needs conversation management.
// - Keep message persistence in a message repository, not directly in this store.

import { create } from 'zustand'

type SessionState = {
  selectedSessionId: string | null
  setSelectedSessionId: (sessionId: string | null) => void
}

export const useSessionStore = create<SessionState>((set) => ({
  selectedSessionId: null,
  setSelectedSessionId: (selectedSessionId) => set({ selectedSessionId }),
}))
