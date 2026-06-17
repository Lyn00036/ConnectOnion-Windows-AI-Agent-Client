declare module 'connectonion-connect' {
  import type {
    AgentStatus,
    ChatItem,
    ConnectionState,
    Response,
  } from 'connectonion/react'

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

  export type RemoteAgentClient = {
    readonly address: string
    readonly agentAddress: string
    readonly status: AgentStatus
    readonly connectionState: ConnectionState
    readonly ui: ChatItem[]
    readonly error: Error | null
    onMessage: (() => void) | null
    input(prompt: string): Promise<Response>
    reset(): void
  }

  export function connect(
    agentAddress: string,
    options?: ConnectOptions,
  ): RemoteAgentClient
}
