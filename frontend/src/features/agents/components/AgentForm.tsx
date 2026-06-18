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

  function handleSubmit(event: SubmitEvent<HTMLFormElement>) {
    event.preventDefault()

    if (!newAgentName.trim() || !newAgentAddress.trim()) return

    onAddAgent(newAgentName, newAgentAddress, newAgentDirectUrl)
    setNewAgentName('')
    setNewAgentAddress('')
    setNewAgentDirectUrl('')
  }

  return (
    <form className="agent-form" onSubmit={handleSubmit}>
      <Input
        aria-label="Agent name"
        onChange={(event) => setNewAgentName(event.target.value)}
        placeholder="Agent name"
        value={newAgentName}
      />
      <Input
        aria-label="Agent address"
        onChange={(event) => setNewAgentAddress(event.target.value)}
        placeholder="0x... or agent name"
        value={newAgentAddress}
      />
      <Input
        aria-label="Direct URL"
        onChange={(event) => setNewAgentDirectUrl(event.target.value)}
        placeholder="Optional direct URL"
        value={newAgentDirectUrl}
      />
      <Button disabled={!newAgentName.trim() || !newAgentAddress.trim()} type="submit">
        Add Agent
      </Button>
    </form>
  )
}
