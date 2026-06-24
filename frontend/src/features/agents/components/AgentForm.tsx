/** Agent form component for adding temporary local agent connection entries. */

import { useState } from 'react'
import type { SubmitEvent } from 'react'

import { Button } from '../../../shared/components/Button'
import { Input } from '../../../shared/components/Input'

type AgentFormProps = {
  onAddAgent: (name: string, address: string, directUrl?: string) => void
}

export function AgentForm({ onAddAgent }: AgentFormProps) {
  const [newAgentName, setNewAgentName] = useState('')
  const [newAgentAddress, setNewAgentAddress] = useState('')
  const [newAgentDirectUrl, setNewAgentDirectUrl] = useState('')
  const hasConnectionTarget = Boolean(
    newAgentAddress.trim() || newAgentDirectUrl.trim(),
  )

  function handleSubmit(event: SubmitEvent<HTMLFormElement>) {
    event.preventDefault()

    if (!newAgentName.trim() || !hasConnectionTarget) return

    onAddAgent(newAgentName, newAgentAddress, newAgentDirectUrl)
    setNewAgentName('')
    setNewAgentAddress('')
    setNewAgentDirectUrl('')
  }

  return (
    <form className="grid gap-2" onSubmit={handleSubmit}>
      <Input
        aria-label="Agent name"
        className="h-10 min-w-0 rounded-md border-0 bg-[#f4f4f4] px-3 text-sm text-[#182230] outline-none focus:ring-2 focus:ring-[#b9a4dc]/30"
        onChange={(event) => setNewAgentName(event.target.value)}
        placeholder="Agent name"
        value={newAgentName}
      />
      <Input
        aria-label="Agent address"
        className="h-10 min-w-0 rounded-md border-0 bg-[#f4f4f4] px-3 text-sm text-[#182230] outline-none focus:ring-2 focus:ring-[#b9a4dc]/30"
        onChange={(event) => setNewAgentAddress(event.target.value)}
        placeholder="0x... (optional with Direct URL)"
        value={newAgentAddress}
      />
      <Input
        aria-label="Direct URL"
        className="h-10 min-w-0 rounded-md border-0 bg-[#f4f4f4] px-3 text-sm text-[#182230] outline-none focus:ring-2 focus:ring-[#b9a4dc]/30"
        onChange={(event) => setNewAgentDirectUrl(event.target.value)}
        pattern="https?://.+"
        placeholder="Optional direct URL"
        title="Use an http:// or https:// agent base URL"
        type="url"
        value={newAgentDirectUrl}
      />
      <Button className="min-h-[38px] rounded-md border-0 bg-[#1e1f22] px-3 text-[13px] font-bold text-white disabled:cursor-not-allowed disabled:opacity-50" disabled={!newAgentName.trim() || !hasConnectionTarget} type="submit">
        Add Agent
      </Button>
    </form>
  )
}
