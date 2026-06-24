/** Shared desktop shell with an agent -> session navigation tree. */

import { useState } from 'react'
import { NavLink, Outlet } from 'react-router-dom'
import { useNavigate } from 'react-router-dom'

import onionLogo from '../assets/icon.png'
import { AgentForm } from '../features/agents/components/AgentForm'
import { useAgentStore } from '../features/agents/store'
import { useSessionStore } from '../features/sessions/store'

type NavIconProps = {
  type: 'chat' | 'sessions' | 'agents' | 'settings'
}

function NavIcon({ type }: NavIconProps) {
  const paths = {
    chat: <path d="M4 5.5A2.5 2.5 0 0 1 6.5 3h11A2.5 2.5 0 0 1 20 5.5v7a2.5 2.5 0 0 1-2.5 2.5H10l-4.5 4v-4A2.5 2.5 0 0 1 3 12.5v-7Z" />,
    sessions: <path d="M5 5h14v14H5zM8 9h8M8 12h8M8 15h5" />,
    agents: <path d="M12 12a4 4 0 1 0 0-8 4 4 0 0 0 0 8Zm-7 8a7 7 0 0 1 14 0" />,
    settings: <path d="M12 15.25A3.25 3.25 0 1 0 12 8.75a3.25 3.25 0 0 0 0 6.5Zm0-12.5v2m0 14.5v2m9.25-9.25h-2M4.75 12h-2m15.79-6.54-1.42 1.42M6.88 17.12l-1.42 1.42m13.08 0-1.42-1.42M6.88 6.88 5.46 5.46" />,
  }

  return (
    <svg aria-hidden="true" className="size-[18px]" fill="none" stroke="currentColor" strokeLinecap="round" strokeLinejoin="round" strokeWidth="1.8" viewBox="0 0 24 24">
      {paths[type]}
    </svg>
  )
}

const navigationClass = (isActive: boolean) =>
  `grid min-h-[38px] grid-cols-[20px_1fr_auto] items-center gap-2 rounded-lg px-2.5 py-2 text-[13px] font-semibold no-underline transition-colors max-[760px]:flex max-[760px]:justify-center max-[760px]:px-0 ${
    isActive
      ? 'bg-[#ededf0] text-[#17181b]'
      : 'text-[#676b74] hover:bg-[#f2f2f4] hover:text-[#202124]'
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
    ensureSession(agentId)
    setExpandedAgentId(agentId)
    navigate('/chat')
  }

  function openSession(agentId: string, sessionId: string) {
    selectAgent(agentId)
    setSelectedSessionId(sessionId)
    setExpandedAgentId(agentId)
    navigate('/chat')
  }

  function createSession(agentId: string) {
    selectAgent(agentId)
    startNewSession(agentId)
    setExpandedAgentId(agentId)
    navigate('/chat')
  }

  function handleAddAgent(name: string, address: string, directUrl?: string) {
    addAgent(name, address, directUrl)
    setExpandedAgentId(useAgentStore.getState().selectedAgentId)
    setShowAddAgent(false)
    navigate('/chat')
  }

  function handleRemoveAgent(agentId: string) {
    removeSessionsForAgent(agentId)
    removeAgent(agentId)
    setExpandedAgentId(useAgentStore.getState().selectedAgentId)
  }

  return (
    <main className="grid min-h-svh grid-cols-[286px_minmax(0,1fr)] bg-white max-[820px]:grid-cols-[230px_minmax(0,1fr)]">
      <aside className="flex h-svh min-w-0 flex-col bg-[#fbfbfc] px-2.5 pb-2.5 pt-3.5">
        <button className="flex min-h-[38px] items-center gap-2.5 border-0 bg-transparent px-1.5 pb-3.5 text-left" onClick={() => navigate('/chat')} type="button">
          <img alt="ConnectOnion" className="size-[29px] object-contain" src={onionLogo} />
          <strong className="text-sm tracking-[-0.01em] text-[#18191c]">ConnectOnion</strong>
          <span className="ml-auto text-[10px] text-[#a0a3aa]">desktop</span>
        </button>

        <nav className="min-h-0 flex-1 overflow-y-auto" aria-label="Agents and sessions">
          {agents.length === 0 && (
            <p className="px-3 py-5 text-xs leading-5 text-[#8a8d95]">Add an agent to start a conversation.</p>
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
                  <div className={`group flex min-h-11 items-center gap-1 rounded-lg px-1.5 transition-colors ${isSelected ? 'bg-[#ededf0]' : 'hover:bg-[#f2f2f4]'}`}>
                    <button aria-label={`${isExpanded ? 'Collapse' : 'Expand'} ${agent.name}`} className="grid size-6 min-h-0 flex-none place-items-center border-0 bg-transparent p-0 text-[#8b8e96]" onClick={() => setExpandedAgentId(isExpanded ? null : agent.id)} type="button">
                      <svg aria-hidden="true" className={`size-3.5 transition-transform ${isExpanded ? 'rotate-90' : ''}`} fill="none" stroke="currentColor" strokeWidth="2" viewBox="0 0 24 24"><path d="m9 18 6-6-6-6" /></svg>
                    </button>
                    <button className="flex min-w-0 flex-1 items-center gap-2 border-0 bg-transparent p-0 text-left" onClick={() => openAgent(agent.id)} type="button">
                      <span className="grid size-6 flex-none place-items-center rounded-md bg-[#191a1d] text-[10px] font-extrabold text-white">{agent.name.slice(0, 1).toUpperCase()}</span>
                      <span className="min-w-0 flex-1 truncate text-[13px] font-semibold text-[#25262a]">{agent.name}</span>
                      <small className="text-[10px] text-[#9a9da5]">{agentSessions.length}</small>
                    </button>
                    <button aria-label={`New session for ${agent.name}`} className="grid size-6 min-h-0 flex-none place-items-center border-0 bg-transparent p-0 text-sm text-[#8b8e96] opacity-0 transition-opacity hover:text-[#25262a] group-hover:opacity-100" onClick={() => createSession(agent.id)} type="button">＋</button>
                    <button aria-label={`Remove ${agent.name}`} className="grid size-6 min-h-0 flex-none place-items-center border-0 bg-transparent p-0 text-sm text-[#a6a8ae] opacity-0 transition-opacity hover:text-red-600 group-hover:opacity-100" onClick={() => handleRemoveAgent(agent.id)} type="button">×</button>
                  </div>

                  {isExpanded && (
                    <div className="ml-[30px] py-1 pl-2">
                      {agentSessions.length === 0 && <span className="block px-2 py-2 text-[10px] text-[#a0a3aa]">No sessions yet</span>}
                      {agentSessions.map((session) => (
                        <div className="group/session flex items-center" key={session.id}>
                          <button className={`flex min-w-0 flex-1 items-center gap-2 rounded-md border-0 px-2 py-2 text-left text-[12px] transition-colors ${selectedSessionId === session.id && isSelected ? 'bg-[#f0edf5] text-[#4f4168]' : 'bg-transparent text-[#666a72] hover:bg-[#f2f2f4] hover:text-[#292a2e]'}`} onClick={() => openSession(agent.id, session.id)} type="button">
                            <svg aria-hidden="true" className="size-3.5 flex-none" fill="none" stroke="currentColor" strokeWidth="1.7" viewBox="0 0 24 24"><path d="M5 5h14v11H9l-4 3v-3H5z" /></svg>
                            <span className="truncate">{session.title}</span>
                          </button>
                          <button aria-label={`Delete ${session.title}`} className="grid size-6 min-h-0 flex-none place-items-center border-0 bg-transparent p-0 text-xs text-[#a6a8ae] opacity-0 hover:text-red-600 group-hover/session:opacity-100" onClick={() => removeSession(session.id)} type="button">×</button>
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
          <div className="mb-2 rounded-xl bg-white p-3 shadow-[0_8px_28px_rgb(25_27_31/8%)]">
            <div className="mb-2 flex items-center justify-between"><strong className="text-xs text-[#28292d]">Add agent</strong><button className="size-6 min-h-0 border-0 bg-transparent p-0 text-[#8b8e96]" onClick={() => setShowAddAgent(false)} type="button">×</button></div>
            <AgentForm onAddAgent={handleAddAgent} />
            {error && <p className="mt-2 text-[10px] text-red-600">{error}</p>}
          </div>
        )}
        <button className="flex min-h-[38px] items-center justify-center gap-2 rounded-lg border-0 bg-transparent text-xs font-semibold text-[#656971] transition-colors hover:bg-white hover:text-[#202124]" onClick={() => setShowAddAgent((current) => !current)} type="button">
          <span>＋</span> Add Agent
        </button>
        <NavLink className={({ isActive }) => `${navigationClass(isActive)} mt-2`} to="/settings">
          <NavIcon type="settings" /><span>Settings</span>
        </NavLink>
      </aside>

      <div className="min-h-svh min-w-0 overflow-hidden"><Outlet /></div>
    </main>
  )
}
