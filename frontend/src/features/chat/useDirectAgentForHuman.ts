/** React adapter for the SDK's direct agent connection mode. */

import { useCallback, useEffect, useState } from 'react'
import { connect } from 'connectonion-connect'
import type { RemoteAgent, SessionState } from 'connectonion'
import type {
  ChatItem,
  UseAgentForHumanReturn,
} from 'connectonion/react'

import {
  getConversationStorageKey,
  readConversationEnvelope,
  writeConversationEnvelope,
} from './conversationStorage'

type PersistedAgentState = {
  messages?: SessionState['messages']
  ui?: ChatItem[]
  session?: SessionState | null
  createdAt?: number
  updatedAt?: number
}

type AgentSnapshot = Pick<
  UseAgentForHumanReturn,
  'connectionState' | 'error' | 'status' | 'ui'
> & {
  session: SessionState | null
}

export type DirectAgentForHumanReturn = UseAgentForHumanReturn & {
  isRestoring: boolean
}

const OMITTED_DATA_URL = '[image data omitted]'

// Chat history can contain screenshots or generated image payloads. Persisting
// raw data URLs quickly fills localStorage, so keep lightweight references only.
function sanitizeForPersistence(value: unknown): unknown {
  if (typeof value === 'string') {
    return value.replace(
      /data:[^;,\s]+;base64,[A-Za-z0-9+/=]+/g,
      OMITTED_DATA_URL,
    )
  }

  if (Array.isArray(value)) {
    return value
      .map(sanitizeForPersistence)
      .filter((item) => item !== OMITTED_DATA_URL)
  }

  if (value && typeof value === 'object') {
    const next: Record<string, unknown> = {}

    for (const [key, item] of Object.entries(value)) {
      if (key === 'images' && Array.isArray(item)) {
        const images = item.filter(
          (image) =>
            typeof image === 'string' &&
            !image.startsWith('data:') &&
            image.length <= 8192,
        )
        if (images.length > 0) next[key] = images
        continue
      }

      if (key === 'dataUrl' && typeof item === 'string' && item.startsWith('data:')) {
        continue
      }

      next[key] = sanitizeForPersistence(item)
    }

    return next
  }

  return value
}

function parsePersistedEnvelope(envelopeJson: string | null): PersistedAgentState | null {
  if (!envelopeJson) return null
  try {
    const parsed = JSON.parse(envelopeJson) as { state?: PersistedAgentState }
    return parsed.state ?? null
  } catch {
    return null
  }
}

function restoreAgent(
  agent: RemoteAgent,
  sessionId: string,
  persisted: PersistedAgentState | null,
) {
  const storedSession = persisted?.session
  const storedMessages = persisted?.messages ?? []

  // The SDK owns these fields at runtime, but restoring a desktop conversation
  // needs to seed them before reconnecting to an existing session.
  agent._currentSession = storedSession
    ? { ...storedSession, session_id: sessionId }
    : { session_id: sessionId, messages: storedMessages }
  agent._chatItems = [...(persisted?.ui ?? [])]
}

function getSnapshot(agent: RemoteAgent): AgentSnapshot {
  return {
    connectionState: agent.connectionState,
    error: agent.error,
    session: agent.currentSession,
    status: agent.status,
    ui: [...agent.ui],
  }
}

class DirectAgentClient {
  private readonly agent: RemoteAgent
  private listener: () => void = () => undefined

  constructor(address: string, directUrl: string) {
    this.agent = connect(address, { directUrl })
  }

  restore(sessionId: string, persisted: PersistedAgentState | null) {
    restoreAgent(this.agent, sessionId, persisted)
    this.listener()
  }

  subscribe(listener: () => void) {
    this.listener = listener
    this.agent.onMessage = listener

    return () => {
      // Reset on unmount so a stale SDK instance cannot keep streaming into a
      // route that no longer owns the session.
      this.agent.onMessage = null
      this.agent.reset()
      this.listener = () => undefined
    }
  }

  snapshot() {
    return getSnapshot(this.agent)
  }

  ensureSession(sessionId: string) {
    this.agent._currentSession = {
      ...(this.agent.currentSession ?? {}),
      session_id: sessionId,
    }
  }

  input(
    sessionId: string,
    prompt: string,
    options?: Parameters<UseAgentForHumanReturn['input']>[1],
  ) {
    this.ensureSession(sessionId)
    this.agent.input(prompt, options).catch(() => {
      this.listener()
    })
  }

  reconnect(sessionId: string) {
    this.ensureSession(sessionId)
    this.agent.reconnect(sessionId).catch((error: unknown) => {
      this.setError(error)
    })
  }

  reset() {
    this.agent.reset()
    this.listener()
  }

  sendMessage(message: Parameters<UseAgentForHumanReturn['sendMessage']>[0]) {
    this.agent.send(message)
  }

  signOnboard(options: Parameters<UseAgentForHumanReturn['signOnboard']>[0]) {
    return this.agent.signOnboard(options)
  }

  setMode(
    sessionId: string,
    mode: Parameters<UseAgentForHumanReturn['setMode']>[0],
    options?: Parameters<UseAgentForHumanReturn['setMode']>[1],
  ) {
    this.ensureSession(sessionId)
    this.agent.setMode(mode, options)
    this.listener()
  }

  checkSessionStatus(sessionId: string) {
    return this.agent.checkSessionStatus(sessionId)
  }

  private setError(error: unknown) {
    this.agent._error = error instanceof Error ? error : new Error(String(error))
    this.listener()
  }
}

export function useDirectAgentForHuman(
  address: string,
  sessionId: string,
  directUrl: string,
): DirectAgentForHumanReturn {
  const storageKey = getConversationStorageKey(address, sessionId)
  const [client] = useState(() => new DirectAgentClient(address, directUrl))
  const [createdAt] = useState(() => Date.now())
  const [snapshot, setSnapshot] = useState<AgentSnapshot>(() => client.snapshot())
  const [isRestoring, setIsRestoring] = useState(true)

  const flush = useCallback(() => {
    const nextSnapshot = client.snapshot()
    setSnapshot(nextSnapshot)
    // Persist both the SDK session and the rendered UI list. The SDK state is
    // used for reconnect; the UI list avoids a blank chat while reconnecting.
    const envelope = JSON.stringify({
      state: sanitizeForPersistence({
        messages: nextSnapshot.session?.messages ?? [],
        ui: nextSnapshot.ui,
        session: nextSnapshot.session,
        createdAt,
        updatedAt: Date.now(),
      }),
      version: 0,
    })
    void writeConversationEnvelope(address, sessionId, envelope)
  }, [address, client, createdAt, sessionId])

  useEffect(() => {
    let cancelled = false

    const unsubscribe = client.subscribe(flush)

    void (async () => {
      const envelopeJson = await readConversationEnvelope(address, sessionId)
      if (cancelled) return

      const persisted = parsePersistedEnvelope(envelopeJson)
      client.restore(sessionId, persisted)
      setSnapshot(client.snapshot())
      setIsRestoring(false)

      const hasStoredConversation = Boolean(
        persisted?.session || persisted?.messages?.length || persisted?.ui?.length,
      )
      if (hasStoredConversation) {
        // Reconnect only after local restore so the screen shows the last known
        // conversation immediately, then resumes any server-side session state.
        client.reconnect(sessionId)
      }
    })()

    return () => {
      cancelled = true
      unsubscribe()
    }
  }, [address, client, flush, sessionId, storageKey])

  function input(
    prompt: string,
    options?: Parameters<UseAgentForHumanReturn['input']>[1],
  ) {
    client.input(sessionId, prompt, options)
  }

  function reconnect() {
    client.reconnect(sessionId)
  }

  function reset() {
    client.reset()
  }

  function setMode(
    mode: Parameters<UseAgentForHumanReturn['setMode']>[0],
    options?: Parameters<UseAgentForHumanReturn['setMode']>[1],
  ) {
    client.setMode(sessionId, mode, options)
  }

  return {
    status: snapshot.status,
    connectionState: snapshot.connectionState,
    ui: snapshot.ui,
    sessionId,
    isProcessing: snapshot.status !== 'idle',
    error: snapshot.error,
    checkSessionStatus: (id) => client.checkSessionStatus(id),
    mode: snapshot.session?.mode ?? 'safe',
    ulwTurns: snapshot.session?.ulw_turns ?? null,
    ulwTurnsUsed: snapshot.session?.ulw_turns_used ?? null,
    input,
    sendMessage: (message) => client.sendMessage(message),
    signOnboard: (options) => client.signOnboard(options),
    setMode,
    reconnect,
    reset,
    isRestoring,
  }
}
