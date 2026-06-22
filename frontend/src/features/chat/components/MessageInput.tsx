/** Composer component for editing and submitting a chat message. */

import type { SubmitEvent } from 'react'

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
    <form className="composer" onSubmit={onSubmit}>
      <textarea
        aria-label="Message"
        disabled={isSending || !selectedAgentName}
        onKeyDown={(event) => {
          if (event.key === 'Enter' && !event.shiftKey) {
            event.preventDefault()
            event.currentTarget.form?.requestSubmit()
          }
        }}
        onChange={(event) => onInputChange(event.target.value)}
        placeholder={
          selectedAgentName ? `Ask ${selectedAgentName}...` : 'Add an agent first'
        }
        value={input}
      />
      <button disabled={isSending || !selectedAgentName || !input.trim()} type="submit">
        Send
      </button>
    </form>
  )
}
