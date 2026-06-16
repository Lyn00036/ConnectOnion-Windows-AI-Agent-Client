/** Message list component that displays chat history and the active pending state. */

import type { ChatMessage as ChatMessageType } from '../types'
import { MessageBubble } from './MessageBubble'

type MessageListProps = {
  isSending: boolean
  messages: ChatMessageType[]
  selectedAgentName: string
}

export function MessageList({
  isSending,
  messages,
  selectedAgentName,
}: MessageListProps) {
  return (
    <div className="messages">
      {messages.map((message) => (
        <MessageBubble key={message.id} message={message} />
      ))}
      {isSending && (
        <article className="message message-agent">
          <span className="message-role">{selectedAgentName}</span>
          <p>Thinking...</p>
        </article>
      )}
    </div>
  )
}
