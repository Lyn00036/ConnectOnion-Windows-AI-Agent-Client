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
    <form className="grid grid-cols-[32px_minmax(0,1fr)_36px] items-center gap-2 rounded-[14px] border-0 bg-white px-3 py-2.5 shadow-[0_8px_28px_rgb(25_27_31/8%)]" onSubmit={onSubmit}>
      <span aria-hidden="true" className="grid size-[29px] place-items-center rounded-full border-0 bg-[#f3f3f3] text-base text-[#999ca4]">＋</span>
      <textarea
        aria-label="Message"
        className="min-h-9 max-h-[140px] resize-none border-0 bg-white px-0.5 py-2 text-xs leading-5 text-[#182230] outline-none focus:ring-0 disabled:cursor-not-allowed disabled:opacity-60"
        disabled={isSending || !selectedAgentName}
        onKeyDown={(event) => {
          if (event.key === 'Enter' && !event.shiftKey) {
            event.preventDefault()
            event.currentTarget.form?.requestSubmit()
          }
        }}
        onChange={(event) => onInputChange(event.target.value)}
        placeholder={
          selectedAgentName ? 'Message this agent...' : 'Add an agent first'
        }
        value={input}
      />
      <button aria-label="Send message" className="size-[34px] min-h-0 rounded-full border-0 bg-[#1b1c1f] p-0 text-lg font-bold text-white disabled:cursor-not-allowed disabled:opacity-40" disabled={isSending || !selectedAgentName || !input.trim()} type="submit">
        ↑
      </button>
    </form>
  )
}
