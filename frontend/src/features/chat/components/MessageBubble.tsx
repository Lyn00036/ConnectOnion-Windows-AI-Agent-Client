/** Single chat message bubble for user and agent responses. */

import type { ChatMessage } from '../types'

type MessageBubbleProps = {
  message: ChatMessage
}

export function MessageBubble({ message }: MessageBubbleProps) {
  return (
    <article className={`message message-${message.role}`}>
      <span className="message-role">
        {message.role === 'user' ? 'You' : message.agentName || 'Agent'}
      </span>
      <p>{message.content}</p>
    </article>
  )
}
