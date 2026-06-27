/** ConnectOnion React SDK boundary for one agent and one conversation. */

import { useState } from 'react'
import type { SubmitEvent } from 'react'
import {
  useAgentForHuman,
  type ApprovalMode,
  type OutgoingMessage,
  type UseAgentForHumanReturn,
} from 'connectonion/react'

import type { AgentConfig } from '../../agents/types'
import { useChatStore } from '../store'
import type { OnboardSubmission } from '../types'
import { useDirectAgentForHuman } from '../useDirectAgentForHuman'
import { ChatWindow } from './ChatWindow'

type AgentConversationProps = {
  agentConfig: AgentConfig
  onMessageSubmitted: (prompt: string) => void
  onNewSession: () => void
  sessionId: string
}

export function AgentConversation({
  agentConfig,
  onMessageSubmitted,
  onNewSession,
  sessionId,
}: AgentConversationProps) {
  if (agentConfig.directUrl) {
    return (
      <DirectAgentConversation
        agentConfig={agentConfig}
        onMessageSubmitted={onMessageSubmitted}
        onNewSession={onNewSession}
        sessionId={sessionId}
      />
    )
  }

  return (
    <RelayAgentConversation
      agentConfig={agentConfig}
      onMessageSubmitted={onMessageSubmitted}
      onNewSession={onNewSession}
      sessionId={sessionId}
    />
  )
}

function RelayAgentConversation(props: AgentConversationProps) {
  const agent = useAgentForHuman(props.agentConfig.address, props.sessionId)
  return <AgentConversationContent {...props} agent={agent} />
}

function DirectAgentConversation(props: AgentConversationProps) {
  const agent = useDirectAgentForHuman(
    props.agentConfig.address,
    props.sessionId,
    props.agentConfig.directUrl!,
  )
  return <AgentConversationContent {...props} agent={agent} />
}

function AgentConversationContent({
  agent,
  agentConfig,
  onMessageSubmitted,
  onNewSession,
}: AgentConversationProps & { agent: UseAgentForHumanReturn }) {
  const composerInput = useChatStore((state) => state.input)
  const setComposerInput = useChatStore((state) => state.setInput)
  const [actionError, setActionError] = useState<string | null>(null)

  function handleSubmit(event: SubmitEvent<HTMLFormElement>) {
    event.preventDefault()

    const prompt = composerInput.trim()
    if (!prompt || agent.isProcessing) return

    setActionError(null)
    setComposerInput('')
    onMessageSubmitted(prompt)
    agent.input(prompt)
  }

  function handleSendMessage(message: OutgoingMessage) {
    try {
      setActionError(null)
      agent.sendMessage(message)
    } catch (error) {
      setActionError(error instanceof Error ? error.message : String(error))
    }
  }

  function handleModeChange(mode: ApprovalMode) {
    agent.setMode(mode, mode === 'ulw' ? { turns: 100 } : undefined)
  }

  function handleSubmitOnboard(options: OnboardSubmission) {
    try {
      setActionError(null)
      agent.sendMessage(agent.signOnboard(options))
    } catch (error) {
      setActionError(error instanceof Error ? error.message : String(error))
    }
  }

  function handleNewConversation() {
    agent.reset()
    setComposerInput('')
    setActionError(null)
    onNewSession()
  }

  function handleReconnect() {
    // Clear any stale send error; the connection error is gated on state below.
    setActionError(null)
    agent.reconnect()
  }

  // A connection error (e.g. "Reconnect timed out") becomes stale once we are
  // connected again — upstream does not clear agent.error on a later successful
  // reconnect, so suppress it here while connected. Send errors still show.
  const connectionError =
    agent.connectionState === 'connected' ? null : agent.error?.message ?? null
  const displayedError = actionError ?? connectionError

  return (
    <ChatWindow
      connectionState={agent.connectionState}
      directUrl={agentConfig.directUrl}
      error={displayedError}
      input={composerInput}
      isSending={agent.isProcessing}
      messages={agent.ui}
      mode={agent.mode}
      onInputChange={setComposerInput}
      onModeChange={handleModeChange}
      onNewConversation={handleNewConversation}
      onReconnect={handleReconnect}
      onSendMessage={handleSendMessage}
      onSubmit={handleSubmit}
      onSubmitOnboard={handleSubmitOnboard}
      selectedAgent={agentConfig}
      sessionId={agent.sessionId}
      status={agent.status}
      ulwTurns={agent.ulwTurns}
      ulwTurnsUsed={agent.ulwTurnsUsed}
    />
  )
}
