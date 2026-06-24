/** Session list for one agent. Message bodies remain owned by the SDK/backend. */

import type { AgentConfig } from '../../agents/types'
import type { SessionSummary } from '../types'

type SessionSidebarProps = {
  agent?: AgentConfig
  onOpenSession: (sessionId: string) => void
  onRemoveSession: (sessionId: string) => void
  selectedSessionId: string | null
  sessions: SessionSummary[]
}

export function SessionSidebar({
  agent,
  onOpenSession,
  onRemoveSession,
  selectedSessionId,
  sessions,
}: SessionSidebarProps) {
  if (!agent) {
    return <div className="grid min-h-[300px] place-content-center gap-2 text-center text-[#92959d]"><strong className="text-[15px] text-[#46484e]">No agent selected</strong><span className="text-xs">Add or select an agent first.</span></div>
  }

  if (sessions.length === 0) {
    return <div className="grid min-h-[300px] place-content-center gap-2 text-center text-[#92959d]"><strong className="text-[15px] text-[#46484e]">No sessions for {agent.name}</strong><span className="text-xs">Start a new conversation to create one.</span></div>
  }

  return (
    <div className="grid gap-2.5">
      {[...sessions]
        .sort((left, right) => right.updatedAt.localeCompare(left.updatedAt))
        .map((session) => (
          <article className={`grid grid-cols-[minmax(0,1fr)_38px] items-center rounded-xl border transition-colors ${session.id === selectedSessionId ? 'border-[#c8b9df] bg-[#faf8fd]' : 'border-[#e8e9ec] bg-white hover:border-[#c8b9df] hover:bg-[#faf8fd]'}`} key={session.id}>
            <button className="flex min-w-0 items-center gap-3 border-0 bg-transparent px-4 py-3.5 text-left" onClick={() => onOpenSession(session.id)} type="button">
              <span className="grid size-8 flex-none place-items-center rounded-lg bg-[#eeeaf5] text-[#78649b]">◫</span>
              <span className="min-w-0">
                <strong className="block truncate text-[13px] text-[#28292d]">{session.title}</strong>
                <small className="mt-1 block truncate text-[10px] text-[#9699a1]">{formatSessionDate(session.updatedAt)} · {session.id.slice(0, 8)}</small>
              </span>
            </button>
            <button aria-label={`Delete ${session.title}`} className="size-[30px] min-h-0 border-0 bg-transparent p-0 text-lg text-[#a74141]" onClick={() => onRemoveSession(session.id)} type="button">×</button>
          </article>
        ))}
    </div>
  )
}

function formatSessionDate(value: string) {
  const date = new Date(value)
  return new Intl.DateTimeFormat(undefined, {
    dateStyle: 'medium',
    timeStyle: 'short',
  }).format(date)
}
