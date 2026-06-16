/** Type definitions for chat messages and chat API payloads. */

export type ChatMessage = {
  id: number
  role: 'user' | 'agent'
  content: string
  agentName?: string
}

export type ChatRequest = {
  message: string
  agent_url: string
}

export type ChatResponse = {
  response: string
}
