/** Shared desktop shell with an agent -> session navigation tree. */

import { useState } from 'react'
import { ChevronRight, MessageSquare, Plus, Settings, X } from 'lucide-react'
import { NavLink, Outlet } from 'react-router-dom'
import { useNavigate } from 'react-router-dom'

import { Button } from '@/components/ui/button'
import { IconButton } from '@/shared/components/IconButton'

import onionLogo from '../assets/icon.png'
import { AgentForm } from '../features/agents/components/AgentForm'
import { useAgentStore } from '../features/agents/store'
import { useSessionStore } from '../features/sessions/store'
import { DesktopTitleBar } from './DesktopTitleBar'

const navigationClass = (isActive: boolean) =>
  `grid min-h-[38px] grid-cols-[20px_1fr_auto] items-center gap-2 rounded-lg px-2.5 py-2 text-[13px] font-semibold no-underline transition-colors max-[760px]:flex max-[760px]:justify-center max-[760px]:px-0 ${
    isActive
      ? 'bg-sidebar-accent text-sidebar-accent-foreground'
      : 'text-muted-foreground hover:bg-sidebar-accent hover:text-sidebar-accent-foreground'
  }`

export function DesktopLayout() {
  const navigate = useNavigate()
  const agents = useAgentStore((state) => state.agents)
  const selectedAgentId = useAgentStore((state) => state.selectedAgentId)
  const error = useAgentStore((state) => state.error)
  const addAgent = useAgentStore((state) => state.addAgent)
  const removeAgent = useAgentStore((state) => state.removeAgent)
  const selectAgent = useAgentStore((state) => state.selectAgent)
  const sessions = useSessionStore((state) => state.sessions)
  const selectedSessionId = useSessionStore((state) => state.selectedSessionId)
  const ensureSession = useSessionStore((state) => state.ensureSession)
  const removeSession = useSessionStore((state) => state.removeSession)
  const removeSessionsForAgent = useSessionStore((state) => state.removeSessionsForAgent)
  const setSelectedSessionId = useSessionStore((state) => state.setSelectedSessionId)
  const startNewSession = useSessionStore((state) => state.startNewSession)
  const [expandedAgentId, setExpandedAgentId] = useState<string | null>(selectedAgentId)
  const [showAddAgent, setShowAddAgent] = useState(false)

  function openAgent(agentId: string) {
    selectAgent(agentId)
    const sessionId = ensureSession(agentId)
    setExpandedAgentId(agentId)
    navigate(`/chat/${sessionId}`)
  }

  function openSession(agentId: string, sessionId: string) {
    selectAgent(agentId)
    setSelectedSessionId(sessionId)
    setExpandedAgentId(agentId)
    navigate(`/chat/${sessionId}`)
  }

  function createSession(agentId: string) {
    selectAgent(agentId)
    const sessionId = startNewSession(agentId)
    setExpandedAgentId(agentId)
    navigate(`/chat/${sessionId}`)
  }

  function handleAddAgent(name: string, address: string, directUrl?: string) {
    addAgent(name, address, directUrl)
    const agentId = useAgentStore.getState().selectedAgentId
    setExpandedAgentId(agentId)
    setShowAddAgent(false)
    if (agentId) {
      const sessionId = ensureSession(agentId)
      navigate(`/chat/${sessionId}`)
    }
  }

  function handleRemoveAgent(agentId: string) {
    removeSessionsForAgent(agentId)
    removeAgent(agentId)
    setExpandedAgentId(useAgentStore.getState().selectedAgentId)
  }

  return (
    <div className="flex h-svh min-h-0 flex-col overflow-hidden bg-background">
      <DesktopTitleBar />
      <main className="grid min-h-0 flex-1 grid-cols-[286px_minmax(0,1fr)] bg-background max-[820px]:grid-cols-[230px_minmax(0,1fr)]">
        <aside className="flex h-full min-w-0 flex-col bg-sidebar px-2.5 pb-2.5 pt-3.5 text-sidebar-foreground">
          <Button className="h-auto min-h-[38px] justify-start gap-2.5 rounded-none px-1.5 pb-3.5 text-left" onClick={() => navigate('/chat')} type="button" variant="ghost">
            <img alt="ConnectOnion" className="size-[29px] object-contain" src={onionLogo} />
            <strong className="text-sm text-sidebar-foreground">ConnectOnion</strong>
            <span className="ml-auto text-[10px] text-muted-foreground">desktop</span>
          </Button>

        <nav className="min-h-0 flex-1 overflow-y-auto" aria-label="Agents and sessions">
          {agents.length === 0 && (
            <p className="px-3 py-5 text-xs leading-5 text-muted-foreground">Add an agent to start a conversation.</p>
          )}
          <div className="grid gap-1">
            {agents.map((agent) => {
              const isExpanded = expandedAgentId === agent.id
              const isSelected = selectedAgentId === agent.id
              const agentSessions = sessions
                .filter((session) => session.agentId === agent.id)
                .sort((left, right) => right.updatedAt.localeCompare(left.updatedAt))

              return (
                <div key={agent.id}>
                  <div className={`group flex min-h-11 items-center gap-1 rounded-lg px-1.5 transition-colors ${isSelected ? 'bg-sidebar-accent' : 'hover:bg-sidebar-accent'}`}>
                    <IconButton className="size-6 min-h-0 flex-none p-0 text-muted-foreground hover:text-sidebar-accent-foreground" label={`${isExpanded ? 'Collapse' : 'Expand'} ${agent.name}`} onClick={() => setExpandedAgentId(isExpanded ? null : agent.id)} size="icon-xs" type="button" variant="ghost">
                      <ChevronRight aria-hidden="true" className={`size-3.5 transition-transform ${isExpanded ? 'rotate-90' : ''}`} />
                    </IconButton>
                    <Button className="h-auto min-w-0 flex-1 justify-start gap-2 rounded-none p-0 text-left hover:bg-transparent" onClick={() => openAgent(agent.id)} type="button" variant="ghost">
                      <span className="grid size-6 flex-none place-items-center rounded-md bg-primary text-[10px] font-extrabold text-primary-foreground">{agent.name.slice(0, 1).toUpperCase()}</span>
                      <span className="min-w-0 flex-1 truncate text-[13px] font-semibold text-sidebar-foreground">{agent.name}</span>
                      <small className="text-[10px] text-muted-foreground">{agentSessions.length}</small>
                    </Button>
                    <IconButton className="size-6 min-h-0 flex-none p-0 text-muted-foreground opacity-0 transition-opacity hover:text-sidebar-accent-foreground group-hover:opacity-100" label={`New session for ${agent.name}`} onClick={() => createSession(agent.id)} size="icon-xs" type="button" variant="ghost"><Plus aria-hidden="true" className="size-3.5" /></IconButton>
                    <IconButton className="size-6 min-h-0 flex-none p-0 text-muted-foreground opacity-0 transition-opacity hover:text-destructive group-hover:opacity-100" label={`Remove ${agent.name}`} onClick={() => handleRemoveAgent(agent.id)} size="icon-xs" type="button" variant="ghost"><X aria-hidden="true" className="size-3.5" /></IconButton>
                  </div>

                  {isExpanded && (
                    <div className="ml-[30px] py-1 pl-2">
                      {agentSessions.length === 0 && <span className="block px-2 py-2 text-[10px] text-muted-foreground">No sessions yet</span>}
                      {agentSessions.map((session) => (
                        <div className="group/session flex items-center" key={session.id}>
                          <Button className={`h-auto min-w-0 flex-1 justify-start gap-2 px-2 py-2 text-left text-[12px] ${selectedSessionId === session.id && isSelected ? 'bg-accent text-accent-foreground' : 'text-muted-foreground hover:bg-sidebar-accent hover:text-sidebar-accent-foreground'}`} onClick={() => openSession(agent.id, session.id)} type="button" variant="ghost">
                            <MessageSquare aria-hidden="true" className="size-3.5 flex-none" />
                            <span className="truncate">{session.title}</span>
                          </Button>
                          <IconButton className="size-6 min-h-0 flex-none p-0 text-muted-foreground opacity-0 hover:text-destructive group-hover/session:opacity-100" label={`Delete ${session.title}`} onClick={() => removeSession(session.id)} size="icon-xs" type="button" variant="ghost"><X aria-hidden="true" className="size-3" /></IconButton>
                        </div>
                      ))}
                    </div>
                  )}
                </div>
              )
            })}
          </div>
        </nav>

        {showAddAgent && (
          <div className="mb-2 rounded-xl bg-card p-3 text-card-foreground shadow-lg">
            <div className="mb-2 flex items-center justify-between"><strong className="text-xs text-card-foreground">Add agent</strong><IconButton className="size-6 min-h-0 p-0 text-muted-foreground" label="Close add agent form" onClick={() => setShowAddAgent(false)} size="icon-xs" type="button" variant="ghost"><X aria-hidden="true" className="size-3.5" /></IconButton></div>
            <AgentForm onAddAgent={handleAddAgent} />
            {error && <p className="mt-2 text-[10px] text-destructive">{error}</p>}
          </div>
        )}
        <Button className="min-h-[38px] text-xs font-semibold text-muted-foreground hover:bg-sidebar-accent hover:text-sidebar-accent-foreground" onClick={() => setShowAddAgent((current) => !current)} variant="ghost">
          <Plus aria-hidden="true" /> Add Agent
        </Button>
        <NavLink className={({ isActive }) => `${navigationClass(isActive)} mt-2`} to="/settings">
          <Settings aria-hidden="true" className="size-[18px]" /><span>Settings</span>
        </NavLink>
      </aside>

        <div className="min-h-0 min-w-0 overflow-hidden"><Outlet /></div>
      </main>
    </div>
  )
}
