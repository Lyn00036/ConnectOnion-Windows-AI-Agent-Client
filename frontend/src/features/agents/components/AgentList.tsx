/** Agent list component for selecting the active local or remote agent. */

import { Button } from '@/components/ui/button'

import type { AgentConfig } from '../types'

type AgentListProps = {
  agents: AgentConfig[]
  selectedAgentId: string | null
  onSelectAgent: (agentId: string) => void
}

export function AgentList({
  agents,
  selectedAgentId,
  onSelectAgent,
}: AgentListProps) {
  return (
    <div className="grid grid-cols-[repeat(auto-fill,minmax(210px,1fr))] gap-3 p-4">
      {agents.length === 0 && (
        <div className="p-3 text-[13px] text-muted-foreground">Add an agent address to begin.</div>
      )}
      {agents.map((agent) => (
        <Button
          className={`flex min-h-24 w-full flex-col items-start justify-center gap-1.5 rounded-xl border p-4 text-left transition-colors ${
            agent.id === selectedAgentId
              ? 'border-primary/40 bg-accent ring-2 ring-primary/15'
              : 'border-border bg-card hover:border-primary/30 hover:bg-accent/50'
          }`}
          key={agent.id}
          onClick={() => onSelectAgent(agent.id)}
          type="button"
          variant="outline"
        >
          <span className="max-w-full truncate text-sm font-bold text-foreground">{agent.name}</span>
          <small className="max-w-full truncate text-xs text-muted-foreground">{agent.directUrl ?? agent.address}</small>
        </Button>
      ))}
    </div>
  )
}
