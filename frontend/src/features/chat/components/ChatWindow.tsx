/** Chat panel component that renders the active agent, messages, errors, and input. */

import type { FormEvent } from 'react'

import type { AgentConfig } from '../../agents/types'
import type { ChatMessage as ChatMessageType } from '../types'
import { MessageInput } from './MessageInput'
import { MessageList } from './MessageList'

type ChatWindowProps = {
  error: string | null
  input: string
  isSending: boolean
  messages: ChatMessageType[]
  onInputChange: (value: string) => void
  onSubmit: (event: FormEvent<HTMLFormElement>) => void
  selectedAgent: AgentConfig | undefined
}

export function ChatWindow({
  error,
  input,
  isSending,
  messages,
  onInputChange,
  onSubmit,
  selectedAgent,
}: ChatWindowProps) {
  return (
    <section className="chat-panel" aria-label="Agent chat">
      <div className="active-agent">
        <div>
          <span>Active agent</span>
          <strong>{selectedAgent?.name ?? 'No agent selected'}</strong>
        </div>
        <code>{selectedAgent?.url ?? 'Add an agent manually to start.'}</code>
      </div>

      <MessageList
        isSending={isSending}
        messages={messages}
        selectedAgentName={selectedAgent?.name ?? 'Agent'}
      />

      {error && <div className="error">{error}</div>}

      <MessageInput
        input={input}
        isSending={isSending}
        onInputChange={onInputChange}
        onSubmit={onSubmit}
        selectedAgentName={selectedAgent?.name}
      />
    </section>
  )
}
