/** Type-safe renderer for every ConnectOnion ChatItem variant. */

import { useState } from 'react'
import type { SubmitEvent } from 'react'
import type { ChatItem, OutgoingMessage } from 'connectonion/react'
import { Check, Copy } from 'lucide-react'
import ReactMarkdown from 'react-markdown'

import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Textarea } from '@/components/ui/textarea'

import type { OnboardSubmission } from '../types'

type MessageBubbleProps = {
  message: ChatItem
  onSendMessage?: (message: OutgoingMessage) => void
  onSubmitOnboard?: (options: OnboardSubmission) => void
  selectedAgentName: string
}

const cardClass =
  'w-[min(720px,90%)] self-start rounded-xl border border-border bg-muted p-3.5 text-left text-sm text-foreground'
const cardHeaderClass = 'mb-2 flex items-center gap-2'
const eyebrowClass = 'text-[10px] font-bold uppercase tracking-wide text-muted-foreground'
const cardTitleClass = 'min-w-0 break-words text-[13px] font-semibold text-foreground'
const bodyClass = 'm-0 whitespace-pre-wrap break-words text-sm leading-6 text-foreground'
const helpClass = 'mt-2 block text-[11px] text-muted-foreground'
const detailsClass = 'mt-2 [&>summary]:cursor-pointer [&>summary]:text-xs [&>summary]:font-semibold [&>summary]:text-muted-foreground'
const preClass = 'mt-2 max-w-full overflow-x-auto whitespace-pre-wrap break-words rounded-lg bg-[#101828] p-2.5 font-mono text-xs leading-5 text-[#f2f4f7]'
const inputClass = 'h-10 min-w-0 rounded-lg border border-input bg-background px-3 text-sm text-foreground outline-none focus-visible:ring-2 focus-visible:ring-ring/50'
const textareaClass = 'min-h-24 w-full resize-y rounded-lg border border-input bg-background p-3 text-sm text-foreground outline-none focus-visible:ring-2 focus-visible:ring-ring/50'
const actionsClass = 'mt-3 flex flex-wrap gap-2'
const actionButtonClass = 'min-h-9 px-3 py-1.5 text-xs font-semibold'
const markdownClass = 'min-w-0 text-sm leading-6 text-foreground [&_a]:text-primary [&_a]:underline [&_code]:rounded [&_code]:bg-foreground/10 [&_code]:px-1 [&_li]:ml-5 [&_ol]:list-decimal [&_p]:my-2 [&_p:first-child]:mt-0 [&_p:last-child]:mb-0 [&_pre]:mt-2 [&_pre]:overflow-x-auto [&_pre]:rounded-lg [&_pre]:bg-[#101828] [&_pre]:p-3 [&_pre]:text-xs [&_pre]:text-[#f2f4f7] [&_ul]:list-disc'

// Conversational (user/agent) text. Theme-aware tokens + user-controlled size var.
const messageTextClass = 'min-w-0 leading-relaxed text-foreground text-[length:var(--co-msg-size,0.9375rem)] [&_a]:text-primary [&_a]:underline [&_code]:rounded [&_code]:bg-foreground/10 [&_code]:px-1 [&_li]:ml-5 [&_ol]:list-decimal [&_p]:my-2 [&_p:first-child]:mt-0 [&_p:last-child]:mb-0 [&_pre]:mt-2 [&_pre]:overflow-x-auto [&_pre]:rounded-lg [&_pre]:bg-[#101828] [&_pre]:p-3 [&_pre]:text-xs [&_pre]:text-[#f2f4f7] [&_ul]:list-disc'

const LONG_MESSAGE_THRESHOLD = 700

function eventTone(status: 'running' | 'done' | 'error') {
  if (status === 'error') return 'border-destructive/20 bg-destructive/10'
  if (status === 'running') return 'border-primary/20 bg-primary/10'
  return 'border-success/20 bg-success/10'
}

export function MessageBubble({
  message,
  onSendMessage,
  onSubmitOnboard,
  selectedAgentName,
}: MessageBubbleProps) {
  switch (message.type) {
    case 'user':
    case 'agent':
      return <ConversationMessage message={message} selectedAgentName={selectedAgentName} />

    case 'thinking':
      return (
        <ActivityCard
          detail={message.content ?? message.model}
          label={message.status === 'running' ? 'Thinking…' : 'Thinking complete'}
          meta={message.duration_ms ? formatDuration(message.duration_ms) : undefined}
          status={message.status}
        />
      )

    case 'tool_call':
      return (
        <article className={`${cardClass} ${eventTone(message.status)}`}>
          <div className={cardHeaderClass}>
            <span className={eyebrowClass}>Tool</span>
            <strong className={cardTitleClass}>{message.name}</strong>
            <Badge className="ml-auto text-[10px] uppercase" variant="outline">{message.status}</Badge>
          </div>
          {message.args && Object.keys(message.args).length > 0 && (
            <details className={detailsClass}>
              <summary>Arguments</summary>
              <pre className={preClass}>{formatValue(message.args)}</pre>
            </details>
          )}
          {message.result && (
            <details className={detailsClass} open={message.status === 'error'}>
              <summary>Result</summary>
              <pre className={preClass}>{message.result}</pre>
            </details>
          )}
          {message.timing_ms !== undefined && (
            <small className={helpClass}>{formatDuration(message.timing_ms)}</small>
          )}
        </article>
      )

    case 'ask_user':
      return <AskUserCard item={message} onSendMessage={onSendMessage} />

    case 'approval_needed':
      return <ApprovalCard item={message} onSendMessage={onSendMessage} />

    case 'plan_review':
      return <PlanReviewCard item={message} onSendMessage={onSendMessage} />

    case 'ulw_turns_reached':
      return <UlwCard item={message} onSendMessage={onSendMessage} />

    case 'onboard_required':
      return <OnboardCard item={message} onSubmit={onSubmitOnboard} />

    case 'onboard_success':
      return (
        <ActivityCard
          detail={message.message}
          label={`Onboarding complete · ${message.level}`}
          status="done"
        />
      )

    case 'intent':
      return (
        <ActivityCard
          detail={message.ack}
          label={message.status === 'analyzing' ? 'Understanding request…' : 'Request understood'}
          status={message.status === 'analyzing' ? 'running' : 'done'}
        />
      )

    case 'eval':
      return (
        <ActivityCard
          detail={message.summary ?? message.expected}
          label={message.status === 'evaluating' ? 'Evaluating result…' : 'Evaluation complete'}
          status={message.passed === false ? 'error' : message.status === 'done' ? 'done' : 'running'}
        />
      )

    case 'compact':
      return (
        <ActivityCard
          detail={message.message ?? message.error}
          label={message.status === 'compacting' ? 'Compacting context…' : 'Context compacted'}
          meta={
            message.context_before !== undefined && message.context_after !== undefined
              ? `${message.context_before} → ${message.context_after}`
              : undefined
          }
          status={message.status === 'compacting' ? 'running' : message.status}
        />
      )

    case 'tool_blocked':
      return (
        <article className={`${cardClass} border-destructive/20 bg-destructive/10`}>
          <div className={cardHeaderClass}>
            <span className={eyebrowClass}>Tool blocked</span>
            <strong className={cardTitleClass}>{message.tool}</strong>
          </div>
          <p className={bodyClass}>{message.message}</p>
          <small className={helpClass}>{message.reason}</small>
          {message.command && <pre className={preClass}>{message.command}</pre>}
        </article>
      )

    case 'files_received':
      return (
        <article className={cardClass}>
          <div className={cardHeaderClass}>
            <span className={eyebrowClass}>Files received</span>
            <strong className={cardTitleClass}>{message.files.length} file(s)</strong>
          </div>
          <div className="mt-2 flex flex-wrap gap-1.5">
            {message.files.map((file) => (
              <Badge className="bg-card text-[11px] text-muted-foreground shadow-sm" key={file.path} title={file.path} variant="secondary">{file.name}</Badge>
            ))}
          </div>
        </article>
      )
  }
}

function ConversationMessage({
  message,
  selectedAgentName,
}: {
  message: Extract<ChatItem, { type: 'user' | 'agent' }>
  selectedAgentName: string
}) {
  const isUser = message.type === 'user'
  const isLong = !isUser && message.content.length > LONG_MESSAGE_THRESHOLD
  const [expanded, setExpanded] = useState(true)
  const [copied, setCopied] = useState(false)

  async function handleCopy() {
    try {
      await navigator.clipboard.writeText(message.content)
      setCopied(true)
      window.setTimeout(() => setCopied(false), 1500)
    } catch {
      // Clipboard may be unavailable (e.g. denied permission); ignore silently.
    }
  }

  return (
    <article
      className={isUser
        ? 'max-w-[78%] self-end rounded-3xl bg-muted px-4 py-2.5 text-left text-foreground'
        : 'group/msg w-full self-start py-3 text-left'}
    >
      {isUser ? (
        <span className="sr-only">You</span>
      ) : (
        <div className="mb-2 flex items-center gap-1">
          <span className="text-xs font-semibold text-foreground">{selectedAgentName}</span>
          <button
            className="inline-flex items-center gap-1 rounded px-1.5 py-0.5 text-[10px] text-muted-foreground opacity-0 transition-opacity hover:bg-accent hover:text-accent-foreground group-hover/msg:opacity-100"
            onClick={handleCopy}
            type="button"
          >
            {copied ? <Check aria-hidden="true" className="size-3" /> : <Copy aria-hidden="true" className="size-3" />}
            {copied ? 'Copied' : 'Copy'}
          </button>
        </div>
      )}

      <div className="relative">
        <div className={`${messageTextClass} ${isLong && !expanded ? 'max-h-44 overflow-hidden' : ''}`}>
          <ReactMarkdown>{message.content}</ReactMarkdown>
        </div>
        {isLong && !expanded && (
          <div className="pointer-events-none absolute inset-x-0 bottom-0 h-12 bg-gradient-to-t from-background to-transparent" />
        )}
      </div>

      {!isUser && (
        <div className="mt-1.5 flex items-center gap-3 text-[11px] font-medium text-muted-foreground">
          <button
            className="inline-flex items-center gap-1 hover:text-foreground"
            onClick={handleCopy}
            type="button"
          >
            {copied ? <Check aria-hidden="true" className="size-3" /> : <Copy aria-hidden="true" className="size-3" />}
            {copied ? 'Copied' : 'Copy'}
          </button>
          {isLong && (
            <button
              className="inline-flex items-center hover:text-foreground"
              onClick={() => setExpanded((value) => !value)}
              type="button"
            >
              {expanded ? 'Collapse' : 'Expand'}
            </button>
          )}
        </div>
      )}

      {'images' in message && message.images?.length ? (
        <div className="mt-3 grid grid-cols-[repeat(auto-fit,minmax(140px,1fr))] gap-2">
          {message.images.map((image) => (
            <img alt="Conversation attachment" className="max-h-80 w-full rounded-xl object-contain" key={image} src={image} />
          ))}
        </div>
      ) : null}

      {message.type === 'user' && message.files?.length ? (
        <div className="mt-2 flex flex-wrap gap-1.5">
          {message.files.map((file) => (
            <Badge className="bg-card text-[11px] text-muted-foreground shadow-sm" key={`${file.name}:${file.size}`} variant="secondary">{file.name}</Badge>
          ))}
        </div>
      ) : null}
    </article>
  )
}

function ActivityCard({
  detail,
  label,
  meta,
  status,
}: {
  detail?: string
  label: string
  meta?: string
  status: 'running' | 'done' | 'error'
}) {
  const dotClass = status === 'running'
    ? 'bg-primary ring-4 ring-primary/20'
    : status === 'error'
      ? 'bg-destructive'
      : 'bg-success'

  return (
    <article className={`grid w-[min(720px,90%)] grid-cols-[auto_minmax(0,1fr)_auto] items-start gap-2.5 self-start rounded-xl border px-3 py-2.5 text-xs text-muted-foreground ${eventTone(status)}`}>
      <span className={`mt-1 size-2 rounded-full ${dotClass}`} />
      <div className="min-w-0">
        <strong className="block text-xs font-semibold text-foreground">{label}</strong>
        {detail && <p className="mt-1 whitespace-pre-wrap break-words text-xs text-muted-foreground">{detail}</p>}
      </div>
      {meta && <small className="text-[10px] text-muted-foreground/80">{meta}</small>}
    </article>
  )
}

function AskUserCard({
  item,
  onSendMessage,
}: {
  item: Extract<ChatItem, { type: 'ask_user' }>
  onSendMessage?: (message: OutgoingMessage) => void
}) {
  const [answer, setAnswer] = useState('')
  const [selectedOptions, setSelectedOptions] = useState<string[]>([])
  const [fieldValues, setFieldValues] = useState<Record<string, string>>({})

  const isAnswered = Boolean(item.answered)

  function toggleOption(option: string) {
    if (!item.multi_select) {
      setAnswer(option)
      return
    }
    setSelectedOptions((current) =>
      current.includes(option)
        ? current.filter((value) => value !== option)
        : [...current, option],
    )
  }

  function handleSubmit(event: SubmitEvent<HTMLFormElement>) {
    event.preventDefault()
    const response = item.fields?.length
      ? JSON.stringify(fieldValues)
      : item.multi_select
        ? selectedOptions.join(', ')
        : answer.trim()

    if (!response) return
    onSendMessage?.({ type: 'ASK_USER_RESPONSE', answer: response })
  }

  return (
    <article className={`${cardClass} shadow-sm`}>
      <div className={cardHeaderClass}>
        <span className={eyebrowClass}>Question</span>
        <strong className={cardTitleClass}>{item.text}</strong>
      </div>
      {isAnswered ? (
        <p className="m-0 text-sm text-success">Answered: {item.answer}</p>
      ) : (
        <form className="grid gap-2.5" onSubmit={handleSubmit}>
          {item.fields?.map((field) => (
            <label className="grid gap-1 text-xs font-semibold text-muted-foreground" key={field.name}>
              <span>{field.label}</span>
              <Input
                autoComplete={field.autocomplete}
                className={inputClass}
                onChange={(event) =>
                  setFieldValues((values) => ({ ...values, [field.name]: event.target.value }))
                }
                placeholder={field.placeholder}
                required={field.required}
                type={field.type ?? 'text'}
                value={fieldValues[field.name] ?? ''}
              />
            </label>
          ))}
          {!item.fields?.length && item.options.length > 0 && (
            <div className="flex flex-wrap gap-2">
              {item.options.map((option) => {
                const selected = item.multi_select
                  ? selectedOptions.includes(option)
                  : answer === option
                return (
                  <Button
                    className={`${actionButtonClass} ${selected ? 'bg-accent text-accent-foreground ring-1 ring-border' : ''}`}
                    key={option}
                    onClick={() => toggleOption(option)}
                    type="button"
                    variant={selected ? 'secondary' : 'outline'}
                  >
                    {option}
                  </Button>
                )
              })}
            </div>
          )}
          {!item.fields?.length && (
            <Input
              aria-label="Answer"
              className={inputClass}
              onChange={(event) => setAnswer(event.target.value)}
              placeholder={item.options.length ? 'Or enter another answer' : 'Type your answer'}
              value={answer}
            />
          )}
          <Button className={actionButtonClass} type="submit">Submit answer</Button>
        </form>
      )}
    </article>
  )
}

function ApprovalCard({
  item,
  onSendMessage,
}: {
  item: Extract<ChatItem, { type: 'approval_needed' }>
  onSendMessage?: (message: OutgoingMessage) => void
}) {
  return (
    <article className={`${cardClass} border-warning/20 bg-warning/10 shadow-sm`}>
      <div className={cardHeaderClass}>
        <span className={eyebrowClass}>Approval required</span>
        <strong className={cardTitleClass}>{item.tool}</strong>
      </div>
      {item.description && <p className={bodyClass}>{item.description}</p>}
      <details className={detailsClass}>
        <summary>Arguments</summary>
        <pre className={preClass}>{formatValue(item.arguments)}</pre>
      </details>
      {item.batch_remaining?.length ? (
        <small className={helpClass}>{item.batch_remaining.length} more tool call(s) in this batch</small>
      ) : null}
      <div className={actionsClass}>
        <Button
          className={actionButtonClass}
          onClick={() => onSendMessage?.({ type: 'APPROVAL_RESPONSE', approved: true, scope: 'once' })}
          type="button"
        >
          Approve once
        </Button>
        <Button
          className={actionButtonClass}
          onClick={() => onSendMessage?.({ type: 'APPROVAL_RESPONSE', approved: true, scope: 'session' })}
          type="button"
          variant="outline"
        >
          Approve session
        </Button>
        <Button
          className={actionButtonClass}
          onClick={() => onSendMessage?.({
            type: 'APPROVAL_RESPONSE',
            approved: false,
            scope: 'once',
            mode: 'reject_soft',
          })}
          type="button"
          variant="destructive"
        >
          Reject
        </Button>
      </div>
    </article>
  )
}

function PlanReviewCard({
  item,
  onSendMessage,
}: {
  item: Extract<ChatItem, { type: 'plan_review' }>
  onSendMessage?: (message: OutgoingMessage) => void
}) {
  const [feedback, setFeedback] = useState('')

  return (
    <article className={`${cardClass} shadow-sm`}>
      <div className={cardHeaderClass}>
        <span className={eyebrowClass}>Plan review</span>
        <strong className={cardTitleClass}>Review before implementation</strong>
      </div>
      <div className={`${markdownClass} mb-3 max-h-80 overflow-y-auto rounded-lg border border-border bg-background p-3`}>
        <ReactMarkdown>{item.plan_content}</ReactMarkdown>
      </div>
      <Textarea
        aria-label="Plan feedback"
        className={textareaClass}
        onChange={(event) => setFeedback(event.target.value)}
        placeholder="Optional feedback or requested changes"
        value={feedback}
      />
      <div className={actionsClass}>
        <Button
          className={actionButtonClass}
          onClick={() => onSendMessage?.({
            type: 'PLAN_REVIEW_RESPONSE',
            message: feedback.trim() || 'Plan approved. Proceed with implementation.',
          })}
          type="button"
        >
          Approve plan
        </Button>
        <Button
          className={actionButtonClass}
          onClick={() => {
            if (!feedback.trim()) return
            onSendMessage?.({ type: 'PLAN_REVIEW_RESPONSE', message: feedback.trim() })
          }}
          type="button"
          variant="outline"
        >
          Request changes
        </Button>
      </div>
    </article>
  )
}

function UlwCard({
  item,
  onSendMessage,
}: {
  item: Extract<ChatItem, { type: 'ulw_turns_reached' }>
  onSendMessage?: (message: OutgoingMessage) => void
}) {
  return (
    <article className={`${cardClass} border-warning/20 bg-warning/10 shadow-sm`}>
      <div className={cardHeaderClass}>
        <span className={eyebrowClass}>ULW checkpoint</span>
        <strong className={cardTitleClass}>{item.turns_used}/{item.max_turns} turns used</strong>
      </div>
      <p className={bodyClass}>The autonomous work budget has been reached.</p>
      <div className={actionsClass}>
        <Button
          className={actionButtonClass}
          onClick={() => onSendMessage?.({ type: 'ULW_RESPONSE', action: 'continue', turns: 10 })}
          type="button"
        >
          Continue 10 turns
        </Button>
        <Button
          className={actionButtonClass}
          onClick={() => onSendMessage?.({ type: 'ULW_RESPONSE', action: 'switch_mode', mode: 'safe' })}
          type="button"
          variant="outline"
        >
          Return to safe mode
        </Button>
      </div>
    </article>
  )
}

function OnboardCard({
  item,
  onSubmit,
}: {
  item: Extract<ChatItem, { type: 'onboard_required' }>
  onSubmit?: (options: OnboardSubmission) => void
}) {
  const [inviteCode, setInviteCode] = useState('')
  const supportsInvite = item.methods.some((method) => method.toLowerCase().includes('invite'))

  return (
    <article className={`${cardClass} border-warning/20 bg-warning/10 shadow-sm`}>
      <div className={cardHeaderClass}>
        <span className={eyebrowClass}>Access required</span>
        <strong className={cardTitleClass}>Agent onboarding</strong>
      </div>
      <p className={bodyClass}>Supported methods: {item.methods.join(', ')}</p>
      {item.paymentAmount !== undefined && <p className={bodyClass}>Payment: {item.paymentAmount}</p>}
      <div className="mt-3 grid gap-2.5">
        {supportsInvite && (
          <Input
            aria-label="Invite code"
            className={inputClass}
            onChange={(event) => setInviteCode(event.target.value)}
            placeholder="Invite code"
            value={inviteCode}
          />
        )}
        <Button
          className={actionButtonClass}
          disabled={supportsInvite && !inviteCode.trim()}
          onClick={() => onSubmit?.({
            inviteCode: inviteCode.trim() || undefined,
            payment: item.paymentAmount,
          })}
          type="button"
        >
          Submit access request
        </Button>
      </div>
      <p className={helpClass}>The SDK signs this request with the current client identity.</p>
    </article>
  )
}

function formatDuration(durationMs: number) {
  return durationMs < 1000 ? `${durationMs} ms` : `${(durationMs / 1000).toFixed(1)} s`
}

function formatValue(value: unknown) {
  return typeof value === 'string' ? value : JSON.stringify(value, null, 2)
}
