/** Session list for one agent. Message bodies remain owned by the SDK/backend. */

import { MessageSquare, Trash2 } from 'lucide-react'

import { Button } from '@/components/ui/button'
import { IconButton } from '@/shared/components/IconButton'

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
    return (
      <div className="grid min-h-[300px] place-content-center gap-2 text-center text-muted-foreground">
        <strong className="text-[15px] text-foreground">No agent selected</strong>
        <span className="text-xs">Add or select an agent first.</span>
      </div>
    )
  }

  if (sessions.length === 0) {
    return (
      <div className="grid min-h-[300px] place-content-center gap-2 text-center text-muted-foreground">
        <strong className="text-[15px] text-foreground">No sessions for {agent.name}</strong>
        <span className="text-xs">Start a new conversation to create one.</span>
      </div>
    )
  }

  return (
    <div className="grid gap-2.5">
      {[...sessions]
        .sort((left, right) => right.updatedAt.localeCompare(left.updatedAt))
        .map((session) => (
          <article
            className={`grid grid-cols-[minmax(0,1fr)_38px] items-center rounded-xl border transition-colors ${
              session.id === selectedSessionId
                ? 'border-primary/40 bg-accent'
                : 'border-border bg-card hover:border-primary/30 hover:bg-accent/50'
            }`}
            key={session.id}
          >
            <Button
              className="h-auto min-w-0 justify-start gap-3 rounded-none px-4 py-3.5 text-left"
              onClick={() => onOpenSession(session.id)}
              type="button"
              variant="ghost"
            >
              <span className="grid size-8 flex-none place-items-center rounded-lg bg-muted text-muted-foreground">
                <MessageSquare aria-hidden="true" className="size-3.5" />
              </span>
              <span className="min-w-0">
                <strong className="block truncate text-[13px] text-foreground">{session.title}</strong>
                <small className="mt-1 block truncate text-[10px] text-muted-foreground">
                  {formatSessionDate(session.updatedAt)} · {session.id.slice(0, 8)}
                </small>
              </span>
            </Button>
            <IconButton
              className="size-[30px] min-h-0 p-0 text-destructive"
              label={`Delete ${session.title}`}
              onClick={() => onRemoveSession(session.id)}
              size="icon-sm"
              type="button"
              variant="ghost"
            >
              <Trash2 aria-hidden="true" className="size-3.5" />
            </IconButton>
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
