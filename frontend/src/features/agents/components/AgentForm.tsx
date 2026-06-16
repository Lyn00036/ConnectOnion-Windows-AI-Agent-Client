/** Agent form component for adding temporary local agent connection entries. */

import { useState } from 'react'
import type { FormEvent } from 'react'

import { Button } from '../../../shared/components/Button'
import { Input } from '../../../shared/components/Input'

type AgentFormProps = {
  onAddAgent: (name: string, url: string) => void
}

export function AgentForm({ onAddAgent }: AgentFormProps) {
  const [newAgentName, setNewAgentName] = useState('')
  const [newAgentUrl, setNewAgentUrl] = useState('')

  function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()

    if (!newAgentName.trim() || !newAgentUrl.trim()) return

    onAddAgent(newAgentName, newAgentUrl)
    setNewAgentName('')
    setNewAgentUrl('')
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
        aria-label="Agent URL"
        onChange={(event) => setNewAgentUrl(event.target.value)}
        placeholder="https://your-agent.example.com"
        value={newAgentUrl}
      />
      <Button disabled={!newAgentName.trim() || !newAgentUrl.trim()} type="submit">
        Add Agent
      </Button>
    </form>
  )
}
