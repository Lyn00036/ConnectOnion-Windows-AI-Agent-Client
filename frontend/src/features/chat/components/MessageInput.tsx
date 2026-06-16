/** Composer component for editing and submitting a chat message. */

import type { FormEvent } from 'react'

type MessageInputProps = {
  input: string
  isSending: boolean
  onInputChange: (value: string) => void
  onSubmit: (event: FormEvent<HTMLFormElement>) => void
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
      <input
        aria-label="Message"
        disabled={isSending || !selectedAgentName}
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
