/** ConnectOnion React SDK boundary for one agent and one conversation. */

import { useState } from 'react'
import type { SubmitEvent } from 'react'
import {
  useAgentForHuman,
  type ApprovalMode,
  type OutgoingMessage,
} from 'connectonion/react'

import type { AgentConfig } from '../../agents/types'
import { useChatStore } from '../store'
import type { OnboardSubmission } from '../types'
import { ChatWindow } from './ChatWindow'

type AgentConversationProps = {
  agentConfig: AgentConfig
  onNewSession: () => void
  sessionId: string
}

export function AgentConversation({
  agentConfig,
  onNewSession,
  sessionId,
}: AgentConversationProps) {
  const composerInput = useChatStore((state) => state.input)
  const setComposerInput = useChatStore((state) => state.setInput)
  const [actionError, setActionError] = useState<string | null>(null)

  const agent = useAgentForHuman(agentConfig.address, sessionId)

  function handleSubmit(event: SubmitEvent<HTMLFormElement>) {
    event.preventDefault()

    const prompt = composerInput.trim()
    if (!prompt || agent.isProcessing) return

    setActionError(null)
    setComposerInput('')
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

  return (
    <ChatWindow
      connectionState={agent.connectionState}
      directUrlUnsupported={Boolean(agentConfig.directUrl)}
      error={actionError ?? agent.error?.message ?? null}
      input={composerInput}
      isSending={agent.isProcessing}
      messages={agent.ui}
      mode={agent.mode}
      onInputChange={setComposerInput}
      onModeChange={handleModeChange}
      onNewConversation={handleNewConversation}
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
