/** Single chat message bubble for user and agent responses. */

import type { ChatItem } from 'connectonion/react'

type MessageBubbleProps = {
  message: ChatItem
  selectedAgentName: string
}

export function MessageBubble({
  message,
  selectedAgentName,
}: MessageBubbleProps) {
  const role = message.type === 'user' ? 'user' : 'agent'
  const label = message.type === 'user' ? 'You' : getAgentLabel(message, selectedAgentName)
  const content = getMessageContent(message)

  return (
    <article className={`message message-${role}`}>
      <span className="message-role">{label}</span>
      <p>{content}</p>
    </article>
  )
}

function getAgentLabel(message: ChatItem, selectedAgentName: string) {
  if (message.type === 'tool_call') return `Tool: ${message.name}`
  if (message.type === 'thinking') return selectedAgentName
  if (message.type === 'ask_user') return selectedAgentName
  if (message.type === 'approval_needed') return `Approval: ${message.tool}`
  return selectedAgentName
}

function getMessageContent(message: ChatItem) {
  switch (message.type) {
    case 'user':
    case 'agent':
      return message.content
    case 'thinking':
      return message.status === 'running' ? 'Thinking...' : message.content ?? message.status
    case 'tool_call':
      return message.result ?? JSON.stringify(message.args ?? {}, null, 2)
    case 'ask_user':
      return message.text
    case 'approval_needed':
      return message.description ?? JSON.stringify(message.arguments, null, 2)
    case 'plan_review':
      return message.plan_content
    case 'tool_blocked':
      return message.message
    case 'onboard_required':
      return `Onboarding required: ${message.methods.join(', ')}`
    case 'onboard_success':
      return message.message
    case 'ulw_turns_reached':
      return `Turn limit reached: ${message.turns_used}/${message.max_turns}`
    case 'files_received':
      return message.files.map((file) => file.name).join(', ')
    case 'compact':
      return message.message ?? message.status
    case 'eval':
      return message.summary ?? message.status
    case 'intent':
      return message.ack ?? message.status
    default:
      return ''
  }
}
