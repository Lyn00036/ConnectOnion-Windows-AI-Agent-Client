/** Chat workspace page that coordinates manual agent selection and messages. */

import type { FormEvent } from 'react'

import { AgentForm } from '../features/agents/components/AgentForm'
import { AgentList } from '../features/agents/components/AgentList'
import { useAgentStore } from '../features/agents/store'
import { ChatWindow } from '../features/chat/components/ChatWindow'
import { sendChatMessage } from '../features/chat/api'
import { useChatStore } from '../features/chat/store'
import { parseUnknownError } from '../shared/api/errors'

export function ChatPage() {
  const agents = useAgentStore((state) => state.agents)
  const selectedAgentId = useAgentStore((state) => state.selectedAgentId)
  const error = useAgentStore((state) => state.error)
  const selectAgent = useAgentStore((state) => state.selectAgent)
  const addAgent = useAgentStore((state) => state.addAgent)
  const removeAgent = useAgentStore((state) => state.removeAgent)
  const setError = useAgentStore((state) => state.setError)

  const messages = useChatStore((state) => state.messages)
  const input = useChatStore((state) => state.input)
  const isSending = useChatStore((state) => state.isSending)
  const setInput = useChatStore((state) => state.setInput)
  const setSending = useChatStore((state) => state.setSending)
  const addMessage = useChatStore((state) => state.addMessage)

  const selectedAgent =
    agents.find((agent) => agent.id === selectedAgentId) ?? undefined

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()

    const message = input.trim()
    if (!message || !selectedAgent || isSending) return

    addMessage({
      id: Date.now(),
      role: 'user',
      content: message,
    })
    setInput('')
    setError(null)
    setSending(true)

    try {
      const data = await sendChatMessage({
        message,
        agent_url: selectedAgent.url,
      })

      addMessage({
        id: Date.now() + 1,
        role: 'agent',
        content: data.response || '(empty response)',
        agentName: selectedAgent.name,
      })
    } catch (err) {
      setError(parseUnknownError(err))
    } finally {
      setSending(false)
    }
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
        error={error}
        input={input}
        isSending={isSending}
        messages={messages}
        onInputChange={setInput}
        onSubmit={handleSubmit}
        selectedAgent={selectedAgent}
      />
    </div>
  )
}
