/** Zustand store for transient chat messages, composer state, and send status. */

import { create } from 'zustand'

import type { ChatMessage } from './types'

type ChatState = {
  messages: ChatMessage[]
  input: string
  isSending: boolean
  addMessage: (message: ChatMessage) => void
  setInput: (input: string) => void
  setSending: (isSending: boolean) => void
}

export const useChatStore = create<ChatState>((set) => ({
  messages: [
    {
      id: 1,
      role: 'agent',
      content: 'Select or add an agent to start chatting.',
      agentName: 'Desktop',
    },
  ],
  input: '',
  isSending: false,
  addMessage: (message) =>
    set((state) => ({ messages: [...state.messages, message] })),
  setInput: (input) => set({ input }),
  setSending: (isSending) => set({ isSending }),
}))
