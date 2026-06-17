/** React bridge for the ConnectOnion TypeScript SDK remote-agent client. */

// TODO(chat-runtime): Turn this hook into the single runtime boundary for agent
// communication. Keep ConnectOnion SDK details here so UI components stay simple.
// Required follow-up:
// - Persist user/agent messages into the current SQLite session.
// - Surface streaming/progress events separately from final messages.
// - Add request timeout, cancel, retry, and reconnect behavior.
// - Normalize SDK errors into user-facing error codes/messages.
// - Support file attachment metadata once drag-and-drop is added.

import { useEffect, useMemo, useRef, useState } from 'react'
import { connect, type RemoteAgentClient } from 'connectonion-connect'
import type { AgentStatus, ChatItem, ConnectionState } from 'connectonion/react'

import type { AgentConfig } from '../agents/types'

type ConnectOnionAgentState = {
  connectionState: ConnectionState
  error: Error | null
  input: (prompt: string) => Promise<void>
  isProcessing: boolean
  reset: () => void
  status: AgentStatus
  ui: ChatItem[]
}

export function useConnectOnionAgent(
  agentConfig: AgentConfig | undefined,
): ConnectOnionAgentState {
  const agentRef = useRef<RemoteAgentClient | null>(null)
  const [ui, setUi] = useState<ChatItem[]>([])
  const [status, setStatus] = useState<AgentStatus>('idle')
  const [connectionState, setConnectionState] =
    useState<ConnectionState>('disconnected')
  const [error, setError] = useState<Error | null>(null)

  const agentKey = useMemo(() => {
    if (!agentConfig) return null
    return `${agentConfig.address}|${agentConfig.directUrl ?? ''}`
  }, [agentConfig])

  useEffect(() => {
    if (!agentConfig || !agentKey) {
      agentRef.current = null
      queueMicrotask(() => {
        setUi([])
        setStatus('idle')
        setConnectionState('disconnected')
        setError(null)
      })
      return
    }

    const remoteAgent = connect(agentConfig.address, {
      directUrl: agentConfig.directUrl,
    })

    function syncAgentState() {
      setUi([...remoteAgent.ui])
      setStatus(remoteAgent.status)
      setConnectionState(remoteAgent.connectionState)
      setError(remoteAgent.error)
    }

    remoteAgent.onMessage = syncAgentState
    agentRef.current = remoteAgent
    queueMicrotask(syncAgentState)

    return () => {
      remoteAgent.onMessage = null
      remoteAgent.reset()
      if (agentRef.current === remoteAgent) {
        agentRef.current = null
      }
    }
  }, [agentConfig, agentKey])

  async function input(prompt: string) {
    if (!agentRef.current) return

    setError(null)
    try {
      await agentRef.current.input(prompt)
      setUi([...agentRef.current.ui])
      setStatus(agentRef.current.status)
      setConnectionState(agentRef.current.connectionState)
      setError(agentRef.current.error)
    } catch (err) {
      setError(err instanceof Error ? err : new Error(String(err)))
    }
  }

  function reset() {
    agentRef.current?.reset()
    setUi([])
    setStatus('idle')
    setConnectionState('disconnected')
    setError(null)
  }

  return {
    connectionState,
    error,
    input,
    isProcessing: status !== 'idle',
    reset,
    status,
    ui,
  }
}
