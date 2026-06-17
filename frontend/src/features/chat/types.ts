/** Type definitions for chat messages and chat API payloads. */

export type ChatMessage = {
  id: number
  role: 'user' | 'agent'
  content: string
  agentName?: string
}
