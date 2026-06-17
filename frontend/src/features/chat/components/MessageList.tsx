/** Message list component that displays chat history and the active pending state. */

import type { ChatItem } from 'connectonion/react'

import { MessageBubble } from './MessageBubble'

type MessageListProps = {
  isSending: boolean
  messages: ChatItem[]
  selectedAgentName: string
}

export function MessageList({
  isSending,
  messages,
  selectedAgentName,
}: MessageListProps) {
  const visibleMessages = messages.filter(isVisibleChatItem)
  const hasRunningThinking = visibleMessages.some(
    (message) => message.type === 'thinking' && message.status === 'running',
  )

  return (
    <div className="messages">
      {visibleMessages.map((message) => (
        <MessageBubble
          key={message.id}
          message={message}
          selectedAgentName={selectedAgentName}
        />
      ))}
      {isSending && !hasRunningThinking && (
        <article className="message message-agent">
          <span className="message-role">{selectedAgentName}</span>
          <p>Thinking...</p>
        </article>
      )}
    </div>
  )
}

function isVisibleChatItem(message: ChatItem) {
  if (message.type === 'thinking') {
    return message.status === 'running' || Boolean(message.content?.trim())
  }

  if (message.type === 'agent') {
    const content = message.content.trim()
    return content !== '' && content.toLowerCase() !== 'done'
  }

  return true
}
