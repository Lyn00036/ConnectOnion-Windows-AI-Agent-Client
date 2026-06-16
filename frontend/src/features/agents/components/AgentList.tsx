/** Agent list component for selecting the active local or remote agent. */

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
    <div className="agent-list">
      {agents.length === 0 && (
        <div className="agent-list-empty">Add an agent URL to begin.</div>
      )}
      {agents.map((agent) => (
        <button
          className={`agent-item ${
            agent.id === selectedAgentId ? 'agent-item-active' : ''
          }`}
          key={agent.id}
          onClick={() => onSelectAgent(agent.id)}
          type="button"
        >
          <span>{agent.name}</span>
          <small>{agent.url}</small>
        </button>
      ))}
    </div>
  )
}
