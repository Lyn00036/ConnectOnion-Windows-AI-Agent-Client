/** Chat panel for SDK state, agent.ui items, actions, and the composer. */

import type { SubmitEvent } from 'react'
import type {
  AgentStatus,
  ApprovalMode,
  ChatItem,
  ConnectionState,
  OutgoingMessage,
} from 'connectonion/react'

import type { AgentConfig } from '../../agents/types'
import type { OnboardSubmission } from '../types'
import { MessageInput } from './MessageInput'
import { MessageList } from './MessageList'

type ChatWindowProps = {
  connectionState?: ConnectionState
  directUrl?: string
  error?: string | null
  input?: string
  isSending?: boolean
  messages?: ChatItem[]
  mode?: ApprovalMode
  onInputChange?: (value: string) => void
  onModeChange?: (mode: ApprovalMode) => void
  onNewConversation?: () => void
  onSendMessage?: (message: OutgoingMessage) => void
  onSubmit?: (event: SubmitEvent<HTMLFormElement>) => void
  onSubmitOnboard?: (options: OnboardSubmission) => void
  selectedAgent: AgentConfig | undefined
  sessionId?: string
  status?: AgentStatus
  ulwTurns?: number | null
  ulwTurnsUsed?: number | null
}

export function ChatWindow({
  connectionState,
  directUrl,
  error,
  input = '',
  isSending = false,
  messages = [],
  mode = 'safe',
  onInputChange,
  onModeChange,
  onNewConversation,
  onSendMessage,
  onSubmit,
  onSubmitOnboard,
  selectedAgent,
  sessionId,
  status = 'idle',
  ulwTurns,
  ulwTurnsUsed,
}: ChatWindowProps) {
  const canChat = Boolean(selectedAgent && onSubmit && onInputChange && onSendMessage)

  return (
    <section className="chat-panel" aria-label="Agent chat">
      <div className="active-agent">
        <div className="active-agent-identity">
          <span>Active agent</span>
          <strong>{selectedAgent?.name ?? 'No agent selected'}</strong>
          {sessionId && <small title={sessionId}>Session {sessionId.slice(0, 8)}</small>}
        </div>
        {selectedAgent ? (
          <div className="agent-controls">
            <span className={`status-pill status-${connectionState ?? 'disconnected'}`}>
              {connectionState ?? 'disconnected'} · {status}
            </span>
            <label className="mode-control">
              <span>Mode</span>
              <select
                aria-label="Agent approval mode"
                onChange={(event) => onModeChange?.(event.target.value as ApprovalMode)}
                value={mode}
              >
                <option value="safe">Safe</option>
                <option value="plan">Plan</option>
                <option value="accept_edits">Accept edits</option>
                <option value="ulw">ULW</option>
              </select>
            </label>
            <button className="new-chat-button" onClick={onNewConversation} type="button">
              New chat
            </button>
          </div>
        ) : (
          <code>Add an agent manually to start.</code>
        )}
      </div>

      {mode === 'ulw' && ulwTurns !== null && ulwTurns !== undefined && (
        <div className="ulw-progress">
          ULW turns: {ulwTurnsUsed ?? 0}/{ulwTurns}
        </div>
      )}

      <MessageList
        isSending={isSending}
        messages={messages}
        onSendMessage={onSendMessage}
        onSubmitOnboard={onSubmitOnboard}
        selectedAgentName={selectedAgent?.name ?? 'Agent'}
      />

      {directUrl && (
        <div className="notice">
          Direct connection: <code>{directUrl}</code>
        </div>
      )}
      {error && <div className="error">{error}</div>}

      <MessageInput
        input={input}
        isSending={isSending}
        onInputChange={onInputChange ?? (() => undefined)}
        onSubmit={onSubmit ?? ((event) => event.preventDefault())}
        selectedAgentName={canChat ? selectedAgent?.name : undefined}
      />
    </section>
  )
}
