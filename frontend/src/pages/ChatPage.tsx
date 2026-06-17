/** Chat workspace page that coordinates manual agent selection and messages. */

import type { FormEvent } from 'react'

import { AgentForm } from '../features/agents/components/AgentForm'
import { AgentList } from '../features/agents/components/AgentList'
import { useAgentStore } from '../features/agents/store'
import { ChatWindow } from '../features/chat/components/ChatWindow'
import { useChatStore } from '../features/chat/store'
import { useConnectOnionAgent } from '../features/chat/useConnectOnionAgent'

export function ChatPage() {
  const agents = useAgentStore((state) => state.agents)
  const selectedAgentId = useAgentStore((state) => state.selectedAgentId)
  const selectAgent = useAgentStore((state) => state.selectAgent)
  const addAgent = useAgentStore((state) => state.addAgent)
  const removeAgent = useAgentStore((state) => state.removeAgent)

  const input = useChatStore((state) => state.input)
  const setInput = useChatStore((state) => state.setInput)

  const selectedAgent =
    agents.find((agent) => agent.id === selectedAgentId) ?? undefined
  const {
    connectionState,
    error,
    input: sendToAgent,
    isProcessing,
    ui,
  } = useConnectOnionAgent(selectedAgent)

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()

    const message = input.trim()
    if (!message || !selectedAgent || isProcessing) return

    setInput('')
    await sendToAgent(message)
  }

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

      <ChatWindow
        connectionState={connectionState}
        error={error ? error.message : null}
        input={input}
        isSending={isProcessing}
        messages={ui}
        onInputChange={setInput}
        onSubmit={handleSubmit}
        selectedAgent={selectedAgent}
      />
    </div>
  )
}
