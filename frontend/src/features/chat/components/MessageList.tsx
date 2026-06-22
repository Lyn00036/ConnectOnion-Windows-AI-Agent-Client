/** Message list that renders the SDK's reactive agent.ui array. */

import { useEffect, useRef } from 'react'
import type { ChatItem, OutgoingMessage } from 'connectonion/react'

import type { OnboardSubmission } from '../types'
import { MessageBubble } from './MessageBubble'

type MessageListProps = {
  isSending: boolean
  messages: ChatItem[]
  onSendMessage?: (message: OutgoingMessage) => void
  onSubmitOnboard?: (options: OnboardSubmission) => void
  selectedAgentName: string
}

export function MessageList({
  isSending,
  messages,
  onSendMessage,
  onSubmitOnboard,
  selectedAgentName,
}: MessageListProps) {
  const endRef = useRef<HTMLDivElement>(null)
  const visibleMessages = messages.filter(isVisibleChatItem)
  const hasRunningThinking = visibleMessages.some(
    (message) => message.type === 'thinking' && message.status === 'running',
  )

  useEffect(() => {
    endRef.current?.scrollIntoView({ block: 'end' })
  }, [visibleMessages.length])

  return (
    <div className="messages" aria-live="polite">
      {visibleMessages.length === 0 && !isSending && (
        <div className="chat-empty">
          <strong>Start a conversation</strong>
          <span>Messages, tool calls, approvals, and questions will appear here.</span>
        </div>
      )}
      {visibleMessages.map((message) => (
        <MessageBubble
          key={message.id}
          message={message}
          onSendMessage={onSendMessage}
          onSubmitOnboard={onSubmitOnboard}
          selectedAgentName={selectedAgentName}
        />
      ))}
      {isSending && !hasRunningThinking && (
        <article className="message message-agent">
          <span className="message-role">{selectedAgentName}</span>
          <p>Thinking...</p>
        </article>
      )}
      <div ref={endRef} />
    </div>
  )
}

function isVisibleChatItem(message: ChatItem) {
  if (message.type === 'thinking') {
    return (
      message.status === 'running' ||
      Boolean(message.content?.trim()) ||
      Boolean(message.duration_ms)
    )
  }

  if (message.type === 'agent') {
    const content = message.content.trim()
    return content !== '' && content.toLowerCase() !== 'done'
  }

  return true
}
