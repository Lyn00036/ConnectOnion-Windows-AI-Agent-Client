/** Chat workspace page that coordinates agent and SDK session selection. */

import { AgentForm } from '../features/agents/components/AgentForm'
import { AgentList } from '../features/agents/components/AgentList'
import { useAgentStore } from '../features/agents/store'
import { AgentConversation } from '../features/chat/components/AgentConversation'
import { ChatWindow } from '../features/chat/components/ChatWindow'
import { useSessionStore } from '../features/sessions/store'

export function ChatPage() {
  const agents = useAgentStore((state) => state.agents)
  const selectedAgentId = useAgentStore((state) => state.selectedAgentId)
  const selectAgent = useAgentStore((state) => state.selectAgent)
  const addAgent = useAgentStore((state) => state.addAgent)
  const removeAgent = useAgentStore((state) => state.removeAgent)

  const sessionId = useSessionStore((state) => state.selectedSessionId)
  const startNewSession = useSessionStore((state) => state.startNewSession)

  const selectedAgent =
    agents.find((agent) => agent.id === selectedAgentId) ?? undefined

  return (
    <div className="workspace">
      <aside className="agent-sidebar" aria-label="Agents">
        <div className="sidebar-header">
          <h2>Agents</h2>
        </div>
        <AgentList
          agents={agents}
          selectedAgentId={selectedAgent?.id ?? null}
          onSelectAgent={selectAgent}
        />
        <AgentForm onAddAgent={addAgent} />
        <button
          className="remove-agent"
          disabled={!selectedAgent}
          onClick={() => selectedAgent && removeAgent(selectedAgent.id)}
          type="button"
        >
          Remove Selected
        </button>
      </aside>

      {selectedAgent ? (
        <AgentConversation
          agentConfig={selectedAgent}
          key={`${selectedAgent.id}:${sessionId}`}
          onNewSession={startNewSession}
          sessionId={sessionId}
        />
      ) : (
        <ChatWindow selectedAgent={undefined} />
      )}
    </div>
  )
}
