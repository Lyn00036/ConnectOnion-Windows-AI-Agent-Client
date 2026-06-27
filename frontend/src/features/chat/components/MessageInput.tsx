/** Composer component for editing and submitting a chat message. */

import type { SubmitEvent } from 'react'
import { Paperclip, Send } from 'lucide-react'

import { Textarea } from '@/components/ui/textarea'
import { IconButton } from '@/shared/components/IconButton'

type MessageInputProps = {
  input: string
  isSending: boolean
  onInputChange: (value: string) => void
  onSubmit: (event: SubmitEvent<HTMLFormElement>) => void
  selectedAgentName?: string
}

export function MessageInput({
  input,
  isSending,
  onInputChange,
  onSubmit,
  selectedAgentName,
}: MessageInputProps) {
  return (
    <form className="grid grid-cols-[32px_minmax(0,1fr)_36px] items-center gap-2 rounded-[14px] border border-border bg-card px-3 py-2.5 shadow-sm" onSubmit={onSubmit}>
      <span aria-hidden="true" className="grid size-[29px] place-items-center rounded-full bg-muted text-muted-foreground">
        <Paperclip className="size-3.5" />
      </span>
      <Textarea
        aria-label="Message"
        className="min-h-9 max-h-[140px] resize-none border-0 bg-transparent px-0.5 py-2 text-xs leading-5 text-foreground shadow-none focus-visible:ring-0"
        disabled={isSending || !selectedAgentName}
        onKeyDown={(event) => {
          if (event.key === 'Enter' && !event.shiftKey) {
            event.preventDefault()
            event.currentTarget.form?.requestSubmit()
          }
        }}
        onChange={(event) => onInputChange(event.target.value)}
        placeholder={selectedAgentName ? 'Message this agent...' : 'Add an agent first'}
        value={input}
      />
      <IconButton
        className="size-[34px] min-h-0 rounded-full p-0"
        disabled={isSending || !selectedAgentName || !input.trim()}
        label="Send message"
        size="icon"
        type="submit"
      >
        <Send aria-hidden="true" className="size-3.5" />
      </IconButton>
    </form>
  )
}
