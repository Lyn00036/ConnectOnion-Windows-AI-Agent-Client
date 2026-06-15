import { useState } from 'react'
import type { FormEvent } from 'react'
import './App.css'

type AgentConfig = {
  id: string
  name: string
  url: string
}

type ChatMessage = {
  id: number
  role: 'user' | 'agent'
  content: string
  agentName?: string
}

const API_BASE_URL = import.meta.env.VITE_API_BASE_URL ?? ''
const DEFAULT_AGENT_URL =
  import.meta.env.VITE_DEFAULT_AGENT_URL ?? 'http://127.0.0.1:8010'
const AGENTS_STORAGE_KEY = 'connectonion.agents'
const SELECTED_AGENT_STORAGE_KEY = 'connectonion.selectedAgentId'

const DEFAULT_AGENTS: AgentConfig[] = [
  {
    id: 'local-agent',
    name: 'Local Agent',
    url: DEFAULT_AGENT_URL,
  },
]

function loadAgents() {
  try {
    const stored = localStorage.getItem(AGENTS_STORAGE_KEY)
    if (!stored) return DEFAULT_AGENTS

    const agents = JSON.parse(stored) as AgentConfig[]
    if (!Array.isArray(agents) || agents.length === 0) return DEFAULT_AGENTS

    return agents.filter((agent) => agent.name && agent.url)
  } catch {
    return DEFAULT_AGENTS
  }
}

function saveAgents(agents: AgentConfig[]) {
  localStorage.setItem(AGENTS_STORAGE_KEY, JSON.stringify(agents))
}

function normalizeUrl(url: string) {
  return url.trim().replace(/\/+$/, '')
}

function App() {
  const [agents, setAgents] = useState<AgentConfig[]>(loadAgents)
  const [selectedAgentId, setSelectedAgentId] = useState(
    () => localStorage.getItem(SELECTED_AGENT_STORAGE_KEY) || DEFAULT_AGENTS[0].id,
  )
  const [newAgentName, setNewAgentName] = useState('')
  const [newAgentUrl, setNewAgentUrl] = useState('')
  const [messages, setMessages] = useState<ChatMessage[]>([
    {
      id: 1,
      role: 'agent',
      content: 'Select or add an agent to start chatting.',
      agentName: 'Desktop',
    },
  ])
  const [input, setInput] = useState('')
  const [isSending, setIsSending] = useState(false)
  const [isChecking, setIsChecking] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [agentStatus, setAgentStatus] = useState<string | null>(null)

  const selectedAgent =
    agents.find((agent) => agent.id === selectedAgentId) || agents[0]

  function selectAgent(agentId: string) {
    setSelectedAgentId(agentId)
    localStorage.setItem(SELECTED_AGENT_STORAGE_KEY, agentId)
    setAgentStatus(null)
    setError(null)
  }

  function handleAddAgent(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()

    const name = newAgentName.trim()
    const url = normalizeUrl(newAgentUrl)
    if (!name || !url) return

    const agent: AgentConfig = {
      id: `${Date.now()}`,
      name,
      url,
    }
    const nextAgents = [...agents, agent]

    setAgents(nextAgents)
    saveAgents(nextAgents)
    selectAgent(agent.id)
    setNewAgentName('')
    setNewAgentUrl('')
  }

  function handleRemoveAgent(agentId: string) {
    if (agents.length === 1) return

    const nextAgents = agents.filter((agent) => agent.id !== agentId)
    const nextSelectedAgent = nextAgents[0]

    setAgents(nextAgents)
    saveAgents(nextAgents)

    if (selectedAgentId === agentId) {
      selectAgent(nextSelectedAgent.id)
    }
  }

  async function handleCheckAgent() {
    if (!selectedAgent || isChecking) return

    setError(null)
    setAgentStatus(null)
    setIsChecking(true)

    try {
      const params = new URLSearchParams({ agent_url: selectedAgent.url })
      const response = await fetch(
        `${API_BASE_URL}/agents/health?${params.toString()}`,
      )

      if (!response.ok) {
        const text = await response.text()
        throw new Error(text || `Health check failed with ${response.status}`)
      }

      setAgentStatus('Connected')
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Health check failed')
    } finally {
      setIsChecking(false)
    }
  }

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()

    const message = input.trim()
    if (!message || !selectedAgent || isSending) return

    const userMessage: ChatMessage = {
      id: Date.now(),
      role: 'user',
      content: message,
    }

    setMessages((current) => [...current, userMessage])
    setInput('')
    setError(null)
    setIsSending(true)

    try {
      const response = await fetch(`${API_BASE_URL}/agents/chat`, {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
        },
        body: JSON.stringify({
          message,
          agent_url: selectedAgent.url,
        }),
      })

      if (!response.ok) {
        const text = await response.text()
        throw new Error(text || `Request failed with ${response.status}`)
      }

      const data = (await response.json()) as { response: string }
      setAgentStatus('Connected')
      setMessages((current) => [
        ...current,
        {
          id: Date.now() + 1,
          role: 'agent',
          content: data.response || '(empty response)',
          agentName: selectedAgent.name,
        },
      ])
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Request failed')
      setAgentStatus(null)
    } finally {
      setIsSending(false)
    }
  }

  return (
    <main className="shell">
      <header className="topbar">
        <div>
          <h1>Agent Console</h1>
          <p>Connect to any agent service that exposes /health and /chat</p>
        </div>
        <span className="status">{agentStatus || 'HTTP'}</span>
      </header>

      <div className="workspace">
        <aside className="agent-sidebar" aria-label="Agents">
          <div className="sidebar-header">
            <h2>Agents</h2>
            <button
              disabled={isChecking || !selectedAgent}
              onClick={handleCheckAgent}
              type="button"
            >
              {isChecking ? 'Checking' : 'Test'}
            </button>
          </div>

          <div className="agent-list">
            {agents.map((agent) => (
              <button
                className={`agent-item ${
                  agent.id === selectedAgent.id ? 'agent-item-active' : ''
                }`}
                key={agent.id}
                onClick={() => selectAgent(agent.id)}
                type="button"
              >
                <span>{agent.name}</span>
                <small>{agent.url}</small>
              </button>
            ))}
          </div>

          <form className="agent-form" onSubmit={handleAddAgent}>
            <input
              aria-label="Agent name"
              onChange={(event) => setNewAgentName(event.target.value)}
              placeholder="Agent name"
              value={newAgentName}
            />
            <input
              aria-label="Agent URL"
              onChange={(event) => setNewAgentUrl(event.target.value)}
              placeholder="http://127.0.0.1:8010"
              value={newAgentUrl}
            />
            <button
              disabled={!newAgentName.trim() || !newAgentUrl.trim()}
              type="submit"
            >
              Add Agent
            </button>
          </form>

          <button
            className="remove-agent"
            disabled={agents.length === 1}
            onClick={() => handleRemoveAgent(selectedAgent.id)}
            type="button"
          >
            Remove Selected
          </button>
        </aside>

        <section className="chat-panel" aria-label="Agent chat">
          <div className="active-agent">
            <div>
              <span>Active agent</span>
              <strong>{selectedAgent.name}</strong>
            </div>
            <code>{selectedAgent.url}</code>
          </div>

          <div className="messages">
            {messages.map((message) => (
              <article
                className={`message message-${message.role}`}
                key={message.id}
              >
                <span className="message-role">
                  {message.role === 'user'
                    ? 'You'
                    : message.agentName || 'Agent'}
                </span>
                <p>{message.content}</p>
              </article>
            ))}
            {isSending && (
              <article className="message message-agent">
                <span className="message-role">{selectedAgent.name}</span>
                <p>Thinking...</p>
              </article>
            )}
          </div>

          {error && <div className="error">{error}</div>}

          <form className="composer" onSubmit={handleSubmit}>
            <input
              aria-label="Message"
              disabled={isSending}
              onChange={(event) => setInput(event.target.value)}
              placeholder={`Ask ${selectedAgent.name}...`}
              value={input}
            />
            <button disabled={isSending || !input.trim()} type="submit">
              Send
            </button>
          </form>
        </section>
      </div>
    </main>
  )
}

export default App
