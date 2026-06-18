/** Chat panel component that renders the active agent, messages, errors, and input. */

// TODO(chat-ui): Add richer desktop feedback without embedding connection logic here.
// Expected UI states:
// - Connecting, connected, disconnected, reconnecting
// - Agent running, streaming, completed, failed
// - Retry/cancel controls for failed or long-running requests
// - Attachment chips/drop zone once file support is introduced

import type { SubmitEvent } from 'react'
import type { ChatItem, ConnectionState } from 'connectonion/react'

import type { AgentConfig } from '../../agents/types'
import { MessageInput } from './MessageInput'
import { MessageList } from './MessageList'

type ChatWindowProps = {
  connectionState: ConnectionState
  error: string | null
  input: string
  isSending: boolean
  messages: ChatItem[]
  onInputChange: (value: string) => void
  onSubmit: (event: SubmitEvent<HTMLFormElement>) => void
  selectedAgent: AgentConfig | undefined
}

export function ChatWindow({
  connectionState,
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
        <code>
          {selectedAgent
            ? `${selectedAgent.directUrl ?? selectedAgent.address} (${connectionState})`
            : 'Add an agent manually to start.'}
        </code>
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
