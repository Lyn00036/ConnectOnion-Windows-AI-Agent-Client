declare module 'connectonion-connect' {
  import type {
    RemoteAgent,
  } from 'connectonion'

  type WebSocketLike = {
    onopen: ((ev?: unknown) => unknown) | null
    onmessage: ((ev: { data: unknown }) => unknown) | null
    onerror: ((ev: unknown) => unknown) | null
    onclose: ((ev: unknown) => unknown) | null
    send(data: unknown): void
    close(): void
  }

  type ConnectOptions = {
    keys?: unknown
    relayUrl?: string
    directUrl?: string
    wsCtor?: new (url: string) => WebSocketLike
  }

  export function connect(
    agentAddress: string,
    options?: ConnectOptions,
  ): RemoteAgent

  export interface AgentInfo {
    address: string
    name?: string
    tools?: string[]
    trust?: string
    version?: string
    model?: string
    online: boolean
  }

  export function fetchAgentInfo(
    agentAddress: string,
    relayUrl?: string,
  ): Promise<AgentInfo>
}
