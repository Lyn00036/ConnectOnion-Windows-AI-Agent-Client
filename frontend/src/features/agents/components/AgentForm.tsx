/** Agent form component for adding temporary local agent connection entries. */

import { useState } from 'react'
import type { SubmitEvent } from 'react'
import { CheckCircle2, Loader2, XCircle } from 'lucide-react'

import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'

import { testAgentConnection, type ConnectionTestResult } from '../connectionTest'
import type { AgentConfig } from '../types'

type AgentFormProps = {
  onAddAgent: (name: string, address: string, directUrl?: string) => void
  editingAgent?: AgentConfig | null
  onUpdateAgent?: (name: string, address: string, directUrl?: string) => void
  onCancelEdit?: () => void
}

export function AgentForm({
  onAddAgent,
  editingAgent,
  onUpdateAgent,
  onCancelEdit,
}: AgentFormProps) {
  const isEditing = Boolean(editingAgent)
  // Initial values are derived from `editingAgent`. The parent passes a `key`
  // tied to the edit target, so switching agents remounts this form with fresh
  // state (React's recommended alternative to syncing props in an effect).
  const [newAgentName, setNewAgentName] = useState(editingAgent?.name ?? '')
  const [newAgentAddress, setNewAgentAddress] = useState(() =>
    editingAgent && editingAgent.address !== editingAgent.directUrl
      ? editingAgent.address
      : '',
  )
  const [newAgentDirectUrl, setNewAgentDirectUrl] = useState(
    editingAgent?.directUrl ?? '',
  )
  const [error, setError] = useState<string | null>(null)
  const [testing, setTesting] = useState(false)
  const [testResult, setTestResult] = useState<ConnectionTestResult | null>(null)
  const hasConnectionTarget = Boolean(
    newAgentAddress.trim() || newAgentDirectUrl.trim(),
  )

  function clearFeedback() {
    if (error) setError(null)
    if (testResult) setTestResult(null)
  }

  function validateDirectUrl(): boolean {
    const directUrl = newAgentDirectUrl.trim()
    if (directUrl && !/^https?:\/\/.+/.test(directUrl)) {
      setError('Please enter a valid http:// or https:// URL.')
      return false
    }
    return true
  }

  async function handleTestConnection() {
    if (!hasConnectionTarget || testing) return
    if (!validateDirectUrl()) return

    setError(null)
    setTestResult(null)
    setTesting(true)
    try {
      const result = await testAgentConnection({
        address: newAgentAddress,
        directUrl: newAgentDirectUrl,
      })
      setTestResult(result)
    } finally {
      setTesting(false)
    }
  }

  function handleSubmit(event: SubmitEvent<HTMLFormElement>) {
    event.preventDefault()

    if (!newAgentName.trim() || !hasConnectionTarget) return
    if (!validateDirectUrl()) return

    setError(null)
    setTestResult(null)

    if (isEditing) {
      onUpdateAgent?.(newAgentName, newAgentAddress, newAgentDirectUrl)
      return
    }

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
        onChange={(event) => {
          setNewAgentAddress(event.target.value)
          clearFeedback()
        }}
        placeholder="0x address (optional)"
        value={newAgentAddress}
      />
      <Input
        aria-label="Direct URL"
        className="h-10 min-w-0 rounded-md border-0 bg-[#f4f4f4] px-3 text-sm text-[#182230] outline-none focus:ring-2 focus:ring-[#b9a4dc]/30"
        onChange={(event) => {
          setNewAgentDirectUrl(event.target.value)
          clearFeedback()
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
      {testResult ? (
        <p className={`flex items-center gap-1.5 text-[12px] ${testResult.ok ? 'text-success' : 'text-destructive'}`} role="status">
          {testResult.ok ? <CheckCircle2 aria-hidden="true" className="size-3.5 flex-none" /> : <XCircle aria-hidden="true" className="size-3.5 flex-none" />}
          <span>{testResult.detail}</span>
        </p>
      ) : null}
      <div className="flex gap-2">
        <Button className="min-h-[38px] flex-1 text-[13px] font-bold" disabled={!hasConnectionTarget || testing} onClick={handleTestConnection} type="button" variant="outline">
          {testing ? (
            <>
              <Loader2 aria-hidden="true" className="animate-spin" /> Testing…
            </>
          ) : (
            'Test connection'
          )}
        </Button>
        <Button className="min-h-[38px] flex-1 text-[13px] font-bold" disabled={!newAgentName.trim() || !hasConnectionTarget} type="submit">
          {isEditing ? 'Save changes' : 'Add Agent'}
        </Button>
      </div>
      {isEditing ? (
        <Button
          className="min-h-[34px] text-[13px] font-bold"
          onClick={onCancelEdit}
          type="button"
          variant="ghost"
        >
          Cancel
        </Button>
      ) : null}
    </form>
  )
}
