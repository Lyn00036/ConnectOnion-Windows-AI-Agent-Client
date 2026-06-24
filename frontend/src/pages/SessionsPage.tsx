/** Conversation picker scoped to the currently selected saved agent. */

import { Plus } from 'lucide-react'
import { useNavigate } from 'react-router-dom'

import { Button } from '@/components/ui/button'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'

import { useAgentStore } from '../features/agents/store'
import { SessionSidebar } from '../features/sessions/components/SessionSidebar'
import { useSessionStore } from '../features/sessions/store'

export function SessionsPage() {
  const navigate = useNavigate()
  const agents = useAgentStore((state) => state.agents)
  const selectedAgentId = useAgentStore((state) => state.selectedAgentId)
  const selectAgent = useAgentStore((state) => state.selectAgent)
  const sessions = useSessionStore((state) => state.sessions)
  const selectedSessionId = useSessionStore((state) => state.selectedSessionId)
  const setSelectedSessionId = useSessionStore((state) => state.setSelectedSessionId)
  const startNewSession = useSessionStore((state) => state.startNewSession)
  const removeSession = useSessionStore((state) => state.removeSession)
  const selectedAgent = agents.find((agent) => agent.id === selectedAgentId)

  function openSession(sessionId: string) {
    setSelectedSessionId(sessionId)
    navigate(`/chat/${sessionId}`)
  }

  function createSession() {
    if (!selectedAgent) return
    const sessionId = startNewSession(selectedAgent.id)
    navigate(`/chat/${sessionId}`)
  }

  return (
    <section className="min-h-svh bg-white px-[clamp(28px,5vw,72px)] py-[54px] max-[760px]:px-5 max-[760px]:py-[30px]">
      <header className="flex items-start justify-between gap-6 border-b border-[#eceef2] pb-[26px]">
        <div>
          <span className="text-[11px] font-extrabold uppercase tracking-[0.12em] text-[#a18ac9]">History</span>
          <h1 className="my-2 text-[clamp(28px,4vw,40px)] font-bold tracking-[-0.04em] text-[#17181b]">Sessions</h1>
          <p className="m-0 text-sm text-[#777b84]">Each agent keeps its own set of conversations.</p>
        </div>
        {selectedAgent && <Button className="min-h-[38px] text-xs font-bold" onClick={createSession} type="button"><Plus aria-hidden="true" /> New session</Button>}
      </header>

      {agents.length > 0 && (
        <label className="my-5 flex items-center gap-3 text-xs text-[#777b84]">
          <span>Showing conversations for</span>
          <Select onValueChange={(value) => value && selectAgent(value)} value={selectedAgentId ?? ''}>
            <SelectTrigger className="h-10 min-w-[190px] bg-white text-sm text-[#182230]"><SelectValue /></SelectTrigger>
            <SelectContent>
              {agents.map((agent) => <SelectItem key={agent.id} value={agent.id}>{agent.name}</SelectItem>)}
            </SelectContent>
          </Select>
        </label>
      )}

      <SessionSidebar
        agent={selectedAgent}
        onOpenSession={openSession}
        onRemoveSession={removeSession}
        selectedSessionId={selectedSessionId}
        sessions={sessions.filter((session) => session.agentId === selectedAgentId)}
      />
    </section>
  )
}
