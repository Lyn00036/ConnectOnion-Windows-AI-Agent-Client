/** Chat workspace that connects the selected agent to its selected conversation. */

import { useEffect } from 'react'
import { useNavigate, useParams } from 'react-router-dom'

import { useAgentStore } from '../features/agents/store'
import { AgentConversation } from '../features/chat/components/AgentConversation'
import { ChatWindow } from '../features/chat/components/ChatWindow'
import { useSessionStore } from '../features/sessions/store'

export function ChatPage() {
  const navigate = useNavigate()
  const { sessionId: routeSessionId } = useParams<{ sessionId?: string }>()
  const agents = useAgentStore((state) => state.agents)
  const selectedAgentId = useAgentStore((state) => state.selectedAgentId)
  const selectAgent = useAgentStore((state) => state.selectAgent)
  const sessions = useSessionStore((state) => state.sessions)
  const selectedSessionId = useSessionStore((state) => state.selectedSessionId)
  const ensureSession = useSessionStore((state) => state.ensureSession)
  const setSelectedSessionId = useSessionStore((state) => state.setSelectedSessionId)
  const startNewSession = useSessionStore((state) => state.startNewSession)
  const touchSession = useSessionStore((state) => state.touchSession)

  const routeSession = sessions.find((session) => session.id === routeSessionId)
  const effectiveAgentId = routeSession?.agentId ?? selectedAgentId
  const selectedAgent =
    agents.find((agent) => agent.id === effectiveAgentId) ?? undefined
  const selectedSession = routeSession ?? sessions.find(
    (session) =>
      session.id === selectedSessionId && session.agentId === selectedAgent?.id,
  )

  useEffect(() => {
    if (routeSession) {
      if (selectedAgentId !== routeSession.agentId) selectAgent(routeSession.agentId)
      if (selectedSessionId !== routeSession.id) setSelectedSessionId(routeSession.id)
      return
    }

    if (!selectedAgent) return

    const sessionId = selectedSession?.id ?? ensureSession(selectedAgent.id)
    navigate(`/chat/${sessionId}`, { replace: true })
  }, [
    ensureSession,
    navigate,
    routeSession,
    selectAgent,
    selectedAgent,
    selectedAgentId,
    selectedSession,
    selectedSessionId,
    setSelectedSessionId,
  ])

  function handleNewSession() {
    if (!selectedAgent) return
    const sessionId = startNewSession(selectedAgent.id)
    navigate(`/chat/${sessionId}`)
  }

  return (
    <div className="h-full w-full">
      {selectedAgent && selectedSession ? (
        <AgentConversation
          agentConfig={selectedAgent}
          key={`${selectedAgent.id}:${selectedSession.id}`}
          onMessageSubmitted={(prompt) => touchSession(selectedSession.id, prompt)}
          onNewSession={handleNewSession}
          sessionId={selectedSession.id}
        />
      ) : (
        <ChatWindow selectedAgent={undefined} />
      )}
    </div>
  )
}
