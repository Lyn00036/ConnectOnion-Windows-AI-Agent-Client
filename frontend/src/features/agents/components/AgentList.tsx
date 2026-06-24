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
        <div className="p-3 text-[13px] text-[#667085]">Add an agent address to begin.</div>
      )}
      {agents.map((agent) => (
        <Button
          className={`flex min-h-24 w-full flex-col items-start justify-center gap-1.5 rounded-xl border p-4 text-left transition-colors ${
            agent.id === selectedAgentId
              ? 'border-[#bca8dd] bg-[#f8f5fc] shadow-[0_0_0_2px_rgb(188_168_221/14%)]'
              : 'border-[#e4e7ec] bg-white hover:border-[#c8b9df]'
          }`}
          key={agent.id}
          onClick={() => onSelectAgent(agent.id)}
          type="button"
          variant="outline"
        >
          <span className="max-w-full truncate text-sm font-bold text-[#182230]">{agent.name}</span>
          <small className="max-w-full truncate text-xs text-[#667085]">{agent.directUrl ?? agent.address}</small>
        </Button>
      ))}
    </div>
  )
}
