/** Zustand store for transient chat messages, composer state, and send status. */

import { create } from 'zustand'

type ChatState = {
  input: string
  isSending: boolean
  setInput: (input: string) => void
  setSending: (isSending: boolean) => void
}

export const useChatStore = create<ChatState>((set) => ({
  input: '',
  isSending: false,
  setInput: (input) => set({ input }),
  setSending: (isSending) => set({ isSending }),
}))
