/** Message list that renders the SDK's reactive agent.ui array. */

import { useEffect, useRef } from 'react'
import type { UIEvent } from 'react'
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
  const containerRef = useRef<HTMLDivElement>(null)
  const endRef = useRef<HTMLDivElement>(null)
  // Whether the user is pinned to the bottom; captured before content grows so a
  // long incoming reply still auto-scrolls, but reading history is not hijacked.
  const atBottomRef = useRef(true)
  const visibleMessages = messages.filter(isVisibleChatItem)
  const hasRunningThinking = visibleMessages.some(
    (message) => message.type === 'thinking' && message.status === 'running',
  )

  const lastMessage = visibleMessages[visibleMessages.length - 1]
  const lastContentLength =
    lastMessage && 'content' in lastMessage && typeof lastMessage.content === 'string'
      ? lastMessage.content.length
      : 0
  const scrollSignal = `${visibleMessages.length}:${lastMessage?.id ?? ''}:${lastContentLength}:${isSending}`

  function handleScroll(event: UIEvent<HTMLDivElement>) {
    const element = event.currentTarget
    atBottomRef.current =
      element.scrollHeight - element.scrollTop - element.clientHeight < 160
  }

  useEffect(() => {
    if (atBottomRef.current) {
      endRef.current?.scrollIntoView({ block: 'end' })
    }
  }, [scrollSignal])

  return (
    <div ref={containerRef} className="mx-auto flex min-h-0 w-full max-w-[820px] flex-1 flex-col gap-3.5 overflow-y-auto px-[clamp(20px,4vw,44px)] py-7" aria-live="polite" onScroll={handleScroll}>
      {visibleMessages.length === 0 && !isSending && (
        <div className="m-auto flex flex-col items-center gap-2 p-6 text-center text-muted-foreground">
          <span className="mb-0.5 grid size-[52px] place-items-center rounded-[14px] bg-primary text-base font-extrabold text-primary-foreground shadow-lg">{selectedAgentName.slice(0, 1).toUpperCase()}</span>
          <strong className="text-sm text-foreground">{selectedAgentName === 'Agent' ? 'Select an agent' : selectedAgentName}</strong>
          <span className="text-[10px]">{selectedAgentName === 'Agent' ? 'Add or choose an agent from the Agents page.' : 'Ready for a new conversation'}</span>
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
        <article className="w-full self-start py-3 text-left">
          <span className="mb-2 block text-xs font-semibold text-foreground">{selectedAgentName}</span>
          <p className="m-0 text-sm text-muted-foreground">Thinking...</p>
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
