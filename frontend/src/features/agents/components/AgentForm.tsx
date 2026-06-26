/** Agent form component for adding temporary local agent connection entries. */

import { useState } from 'react'
import type { SubmitEvent } from 'react'

import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'

type AgentFormProps = {
  onAddAgent: (name: string, address: string, directUrl?: string) => void
}

export function AgentForm({ onAddAgent }: AgentFormProps) {
  const [newAgentName, setNewAgentName] = useState('')
  const [newAgentAddress, setNewAgentAddress] = useState('')
  const [newAgentDirectUrl, setNewAgentDirectUrl] = useState('')
  const [error, setError] = useState<string | null>(null)
  const hasConnectionTarget = Boolean(
    newAgentAddress.trim() || newAgentDirectUrl.trim(),
  )

  function handleSubmit(event: SubmitEvent<HTMLFormElement>) {
    event.preventDefault()

    if (!newAgentName.trim() || !hasConnectionTarget) return

    const directUrl = newAgentDirectUrl.trim()
    if (directUrl && !/^https?:\/\/.+/.test(directUrl)) {
      setError('Please enter a valid http:// or https:// URL.')
      return
    }

    setError(null)
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
        placeholder="0x address (optional)"
        value={newAgentAddress}
      />
      <Input
        aria-label="Direct URL"
        className="h-10 min-w-0 rounded-md border-0 bg-[#f4f4f4] px-3 text-sm text-[#182230] outline-none focus:ring-2 focus:ring-[#b9a4dc]/30"
        onChange={(event) => {
          setNewAgentDirectUrl(event.target.value)
          if (error) setError(null)
        }}
        placeholder="Optional direct URL"
        title="Use an http:// or https:// agent base URL"
        value={newAgentDirectUrl}
      />
      {error ? (
        <p className="text-[13px] text-[#d92d20]" role="alert">
          {error}
        </p>
      ) : null}
      <Button className="min-h-[38px] text-[13px] font-bold" disabled={!newAgentName.trim() || !hasConnectionTarget} type="submit">
        Add Agent
      </Button>
    </form>
  )
}
