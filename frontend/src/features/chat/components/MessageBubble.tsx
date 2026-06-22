/** Type-safe renderer for every ConnectOnion ChatItem variant. */

import { useState } from 'react'
import type { SubmitEvent } from 'react'
import type { ChatItem, OutgoingMessage } from 'connectonion/react'
import ReactMarkdown from 'react-markdown'

import type { OnboardSubmission } from '../types'

type MessageBubbleProps = {
  message: ChatItem
  onSendMessage?: (message: OutgoingMessage) => void
  onSubmitOnboard?: (options: OnboardSubmission) => void
  selectedAgentName: string
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
      return (
        <article className={`message message-${message.type}`}>
          <span className="message-role">
            {message.type === 'user' ? 'You' : selectedAgentName}
          </span>
          <div className="message-markdown">
            <ReactMarkdown>{message.content}</ReactMarkdown>
          </div>
          {'images' in message && message.images?.length ? (
            <div className="message-images">
              {message.images.map((image) => (
                <img alt="Conversation attachment" key={image} src={image} />
              ))}
            </div>
          ) : null}
          {message.type === 'user' && message.files?.length ? (
            <div className="file-chips">
              {message.files.map((file) => (
                <span key={`${file.name}:${file.size}`}>{file.name}</span>
              ))}
            </div>
          ) : null}
        </article>
      )

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
        <article className={`event-card tool-card event-${message.status}`}>
          <div className="event-card-header">
            <span>Tool</span>
            <strong>{message.name}</strong>
            <small>{message.status}</small>
          </div>
          {message.args && Object.keys(message.args).length > 0 && (
            <details>
              <summary>Arguments</summary>
              <pre>{formatValue(message.args)}</pre>
            </details>
          )}
          {message.result && (
            <details open={message.status === 'error'}>
              <summary>Result</summary>
              <pre>{message.result}</pre>
            </details>
          )}
          {message.timing_ms !== undefined && (
            <small className="event-meta">{formatDuration(message.timing_ms)}</small>
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
        <article className="event-card event-error">
          <div className="event-card-header">
            <span>Tool blocked</span>
            <strong>{message.tool}</strong>
          </div>
          <p>{message.message}</p>
          <small className="event-help">{message.reason}</small>
          {message.command && <pre>{message.command}</pre>}
        </article>
      )

    case 'files_received':
      return (
        <article className="event-card">
          <div className="event-card-header">
            <span>Files received</span>
            <strong>{message.files.length} file(s)</strong>
          </div>
          <div className="file-chips">
            {message.files.map((file) => (
              <span key={file.path} title={file.path}>{file.name}</span>
            ))}
          </div>
        </article>
      )
  }
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
  return (
    <article className={`activity-card event-${status}`}>
      <span className="activity-dot" />
      <div>
        <strong>{label}</strong>
        {detail && <p>{detail}</p>}
      </div>
      {meta && <small>{meta}</small>}
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
    <article className="event-card interaction-card">
      <div className="event-card-header">
        <span>Question</span>
        <strong>{item.text}</strong>
      </div>
      {isAnswered ? (
        <p className="answered-value">Answered: {item.answer}</p>
      ) : (
        <form className="interaction-form" onSubmit={handleSubmit}>
          {item.fields?.map((field) => (
            <label key={field.name}>
              <span>{field.label}</span>
              <input
                autoComplete={field.autocomplete}
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
            <div className="option-grid">
              {item.options.map((option) => {
                const selected = item.multi_select
                  ? selectedOptions.includes(option)
                  : answer === option
                return (
                  <button
                    className={selected ? 'option-selected' : ''}
                    key={option}
                    onClick={() => toggleOption(option)}
                    type="button"
                  >
                    {option}
                  </button>
                )
              })}
            </div>
          )}
          {!item.fields?.length && (
            <input
              aria-label="Answer"
              onChange={(event) => setAnswer(event.target.value)}
              placeholder={item.options.length ? 'Or enter another answer' : 'Type your answer'}
              value={answer}
            />
          )}
          <button className="primary-action" type="submit">Submit answer</button>
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
    <article className="event-card interaction-card event-warning">
      <div className="event-card-header">
        <span>Approval required</span>
        <strong>{item.tool}</strong>
      </div>
      {item.description && <p>{item.description}</p>}
      <details>
        <summary>Arguments</summary>
        <pre>{formatValue(item.arguments)}</pre>
      </details>
      {item.batch_remaining?.length ? (
        <small className="event-help">{item.batch_remaining.length} more tool call(s) in this batch</small>
      ) : null}
      <div className="interaction-actions">
        <button
          className="primary-action"
          onClick={() => onSendMessage?.({ type: 'APPROVAL_RESPONSE', approved: true, scope: 'once' })}
          type="button"
        >
          Approve once
        </button>
        <button
          onClick={() => onSendMessage?.({ type: 'APPROVAL_RESPONSE', approved: true, scope: 'session' })}
          type="button"
        >
          Approve session
        </button>
        <button
          className="danger-action"
          onClick={() => onSendMessage?.({
            type: 'APPROVAL_RESPONSE',
            approved: false,
            scope: 'once',
            mode: 'reject_soft',
          })}
          type="button"
        >
          Reject
        </button>
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
    <article className="event-card interaction-card plan-card">
      <div className="event-card-header">
        <span>Plan review</span>
        <strong>Review before implementation</strong>
      </div>
      <div className="message-markdown plan-content">
        <ReactMarkdown>{item.plan_content}</ReactMarkdown>
      </div>
      <textarea
        aria-label="Plan feedback"
        onChange={(event) => setFeedback(event.target.value)}
        placeholder="Optional feedback or requested changes"
        value={feedback}
      />
      <div className="interaction-actions">
        <button
          className="primary-action"
          onClick={() => onSendMessage?.({
            type: 'PLAN_REVIEW_RESPONSE',
            message: feedback.trim() || 'Plan approved. Proceed with implementation.',
          })}
          type="button"
        >
          Approve plan
        </button>
        <button
          onClick={() => {
            if (!feedback.trim()) return
            onSendMessage?.({ type: 'PLAN_REVIEW_RESPONSE', message: feedback.trim() })
          }}
          type="button"
        >
          Request changes
        </button>
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
    <article className="event-card interaction-card event-warning">
      <div className="event-card-header">
        <span>ULW checkpoint</span>
        <strong>{item.turns_used}/{item.max_turns} turns used</strong>
      </div>
      <p>The autonomous work budget has been reached.</p>
      <div className="interaction-actions">
        <button
          className="primary-action"
          onClick={() => onSendMessage?.({ type: 'ULW_RESPONSE', action: 'continue', turns: 10 })}
          type="button"
        >
          Continue 10 turns
        </button>
        <button
          onClick={() => onSendMessage?.({ type: 'ULW_RESPONSE', action: 'switch_mode', mode: 'safe' })}
          type="button"
        >
          Return to safe mode
        </button>
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
    <article className="event-card interaction-card event-warning">
      <div className="event-card-header">
        <span>Access required</span>
        <strong>Agent onboarding</strong>
      </div>
      <p>Supported methods: {item.methods.join(', ')}</p>
      {item.paymentAmount !== undefined && <p>Payment: {item.paymentAmount}</p>}
      <div className="interaction-form">
        {supportsInvite && (
          <input
            aria-label="Invite code"
            onChange={(event) => setInviteCode(event.target.value)}
            placeholder="Invite code"
            value={inviteCode}
          />
        )}
        <button
          className="primary-action"
          disabled={supportsInvite && !inviteCode.trim()}
          onClick={() => onSubmit?.({
            inviteCode: inviteCode.trim() || undefined,
            payment: item.paymentAmount,
          })}
          type="button"
        >
          Submit access request
        </button>
      </div>
      <p className="event-help">The SDK signs this request with the current client identity.</p>
    </article>
  )
}

function formatDuration(durationMs: number) {
  return durationMs < 1000 ? `${durationMs} ms` : `${(durationMs / 1000).toFixed(1)} s`
}

function formatValue(value: unknown) {
  return typeof value === 'string' ? value : JSON.stringify(value, null, 2)
}
