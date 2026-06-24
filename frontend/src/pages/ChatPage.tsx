/** Chat workspace that connects the selected agent to its selected conversation. */

import { useEffect } from 'react'

import { useAgentStore } from '../features/agents/store'
import { AgentConversation } from '../features/chat/components/AgentConversation'
import { ChatWindow } from '../features/chat/components/ChatWindow'
import { useSessionStore } from '../features/sessions/store'

export function ChatPage() {
  const agents = useAgentStore((state) => state.agents)
  const selectedAgentId = useAgentStore((state) => state.selectedAgentId)
  const sessions = useSessionStore((state) => state.sessions)
  const sessionId = useSessionStore((state) => state.selectedSessionId)
  const ensureSession = useSessionStore((state) => state.ensureSession)
  const startNewSession = useSessionStore((state) => state.startNewSession)
  const touchSession = useSessionStore((state) => state.touchSession)

  const selectedAgent =
    agents.find((agent) => agent.id === selectedAgentId) ?? undefined
  const selectedSession = sessions.find(
    (session) => session.id === sessionId && session.agentId === selectedAgent?.id,
  )

  useEffect(() => {
    if (selectedAgent && !selectedSession) ensureSession(selectedAgent.id)
  }, [ensureSession, selectedAgent, selectedSession])

  return (
    <div className="h-svh w-full">
      {selectedAgent && selectedSession ? (
        <AgentConversation
          agentConfig={selectedAgent}
          key={`${selectedAgent.id}:${selectedSession.id}`}
          onMessageSubmitted={(prompt) => touchSession(selectedSession.id, prompt)}
          onNewSession={() => startNewSession(selectedAgent.id)}
          sessionId={selectedSession.id}
        />
      ) : (
        <ChatWindow selectedAgent={undefined} />
      )}
    </div>
  )
}
