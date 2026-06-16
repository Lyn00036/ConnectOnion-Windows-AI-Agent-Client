/** Type definitions for agent configuration and agent health payloads. */

export type AgentConfig = {
  id: string
  name: string
  url: string
}

export type AgentHealthResponse = {
  status: string
  agent: unknown
}
