/** Chat panel for SDK state, agent.ui items, actions, and the composer. */

import type { SubmitEvent } from 'react'
import { Plus, RefreshCw } from 'lucide-react'
import type {
  AgentStatus,
  ApprovalMode,
  ChatItem,
  ConnectionState,
  OutgoingMessage,
} from 'connectonion/react'

import { Button } from '@/components/ui/button'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'

import type { AgentConfig } from '../../agents/types'
import type { OnboardSubmission } from '../types'
import { MessageInput } from './MessageInput'
import { MessageList } from './MessageList'

type ChatWindowProps = {
  connectionState?: ConnectionState
  directUrl?: string
  error?: string | null
  input?: string
  isSending?: boolean
  messages?: ChatItem[]
  mode?: ApprovalMode
  onInputChange?: (value: string) => void
  onModeChange?: (mode: ApprovalMode) => void
  onNewConversation?: () => void
  onReconnect?: () => void
  onSendMessage?: (message: OutgoingMessage) => void
  onSubmit?: (event: SubmitEvent<HTMLFormElement>) => void
  onSubmitOnboard?: (options: OnboardSubmission) => void
  selectedAgent: AgentConfig | undefined
  sessionId?: string
  status?: AgentStatus
  ulwTurns?: number | null
  ulwTurnsUsed?: number | null
}

export function ChatWindow({
  connectionState,
  directUrl,
  error,
  input = '',
  isSending = false,
  messages = [],
  mode = 'safe',
  onInputChange,
  onModeChange,
  onNewConversation,
  onReconnect,
  onSendMessage,
  onSubmit,
  onSubmitOnboard,
  selectedAgent,
  sessionId,
  status = 'idle',
  ulwTurns,
  ulwTurnsUsed,
}: ChatWindowProps) {
  const canChat = Boolean(selectedAgent && onSubmit && onInputChange && onSendMessage)
  const isNewConversation = messages.length === 0 && !isSending
  const connection = getConnectionMeta(connectionState)
  const canReconnect = Boolean(onReconnect) && connectionState !== undefined && connectionState !== 'connected'

  return (
    <section className="relative flex h-full min-w-0 flex-col overflow-hidden bg-background" aria-label="Agent chat">
      {selectedAgent && (
        <div className="flex min-h-[58px] items-center justify-between gap-3.5 bg-background/90 px-[18px] py-2">
          <div className="flex min-w-0 items-center gap-2.5">
            <span className="grid size-[30px] flex-none place-items-center rounded-lg bg-primary text-xs font-extrabold text-primary-foreground">{selectedAgent.name.slice(0, 1).toUpperCase()}</span>
            <span className="min-w-0">
              <strong className="block truncate text-xs text-foreground">{selectedAgent.name}</strong>
              {sessionId && <small className="mt-0.5 block text-[9px] text-muted-foreground">Session {sessionId.slice(0, 8)}</small>}
            </span>
          </div>
          <div className="flex items-center gap-2">
            <span className={`flex items-center gap-2 rounded-full border px-3 py-1.5 text-xs font-semibold ${connection.bgClass} max-[620px]:px-2`} title={`Connection: ${connection.label} · Agent: ${status}`}>
              <span className={`size-2.5 rounded-full ${connection.dotClass}`} />
              <span className={connection.textClass}>{connection.label}</span>
              <span className="font-medium text-muted-foreground max-[620px]:hidden">· {status}</span>
            </span>
            {canReconnect && (
              <Button className="min-h-[30px] px-2.5 text-[10px] font-bold" disabled={connectionState === 'reconnecting'} onClick={onReconnect} size="sm" type="button" variant="outline">
                <RefreshCw aria-hidden="true" className={connectionState === 'reconnecting' ? 'animate-spin' : ''} />
                {connectionState === 'reconnecting' ? 'Reconnecting' : 'Reconnect'}
              </Button>
            )}
            <Button className="min-h-[30px] bg-secondary px-2.5 text-[10px] font-bold text-secondary-foreground hover:bg-accent" onClick={onNewConversation} size="sm" type="button" variant="secondary">
              <Plus aria-hidden="true" /> New chat
            </Button>
          </div>
        </div>
      )}

      {mode === 'ulw' && ulwTurns !== null && ulwTurns !== undefined && (
        <div className="bg-warning/10 px-5 py-2 text-xs text-warning">ULW turns: {ulwTurnsUsed ?? 0}/{ulwTurns}</div>
      )}

      <MessageList
        isSending={isSending}
        messages={messages}
        onSendMessage={onSendMessage}
        onSubmitOnboard={onSubmitOnboard}
        selectedAgentName={selectedAgent?.name ?? 'Agent'}
      />

      <div className="mx-auto w-[min(760px,calc(100%_-_40px))] pb-[18px] max-[620px]:w-[calc(100%_-_24px)] max-[620px]:pb-2.5">
        {isNewConversation && canChat && (
          <div className="mb-2.5 flex flex-wrap justify-center gap-2" aria-label="Suggested prompts">
            {['What can you do?', 'Show system info', 'List files in current directory'].map((suggestion) => (
              <Button className="min-h-[27px] rounded-full px-2.5 text-[9px] font-medium text-muted-foreground hover:text-foreground last:max-[620px]:hidden" key={suggestion} onClick={() => onInputChange?.(suggestion)} size="xs" type="button" variant="secondary">
                {suggestion}
              </Button>
            ))}
          </div>
        )}
        {directUrl && <div className="mx-1 mb-1.5 truncate text-right text-[8px] text-muted-foreground" title={directUrl}>Direct · {directUrl}</div>}
        {error && <div className="mb-2 rounded-lg bg-destructive/10 p-2.5 text-[10px] text-destructive">{error}</div>}
        <MessageInput
          input={input}
          isSending={isSending}
          onInputChange={onInputChange ?? (() => undefined)}
          onSubmit={onSubmit ?? ((event) => event.preventDefault())}
          selectedAgentName={canChat ? selectedAgent?.name : undefined}
        />
        {selectedAgent && (
          <div className="flex min-h-6 items-center justify-end gap-2.5 px-1 pt-1 text-[8px] text-muted-foreground">
            <label className="flex items-center gap-1">
              <span className="text-[8px] text-muted-foreground">Mode</span>
              <Select onValueChange={(value) => value && onModeChange?.(value as ApprovalMode)} value={mode}>
                <SelectTrigger aria-label="Agent approval mode" className="h-[22px] w-auto border-0 bg-transparent px-1 text-[8px] text-muted-foreground shadow-none" size="sm">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem value="safe">Safe</SelectItem>
                  <SelectItem value="plan">Plan</SelectItem>
                  <SelectItem value="accept_edits">Accept edits</SelectItem>
                  <SelectItem value="ulw">ULW</SelectItem>
                </SelectContent>
              </Select>
            </label>
            <span className="max-[620px]:hidden">Enter to send · Shift + Enter for a new line</span>
          </div>
        )}
      </div>
    </section>
  )
}

// Maps the SDK's ConnectionState ('disconnected' | 'connected' | 'reconnecting')
// to a readable label, colored dot and text tone.
function getConnectionMeta(state?: ConnectionState) {
  if (state === 'connected') {
    return { label: 'Connected', dotClass: 'bg-success', textClass: 'text-success', bgClass: 'border-success/30 bg-success/10' }
  }
  if (state === 'reconnecting') {
    return { label: 'Reconnecting…', dotClass: 'bg-warning animate-pulse', textClass: 'text-warning', bgClass: 'border-warning/30 bg-warning/10' }
  }
  return { label: 'Disconnected', dotClass: 'bg-muted-foreground', textClass: 'text-muted-foreground', bgClass: 'border-border bg-muted' }
}
