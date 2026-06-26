/** Inline key-capture control for rebinding a single keyboard shortcut. */

import { useState, type KeyboardEvent as ReactKeyboardEvent } from 'react'

import { Button } from '@/components/ui/button'

import { eventToAccelerator } from '../shortcuts'

type ShortcutRecorderProps = {
  accelerator: string
  isCustom: boolean
  onChange: (accelerator: string) => void
  onReset: () => void
}

export function ShortcutRecorder({ accelerator, isCustom, onChange, onReset }: ShortcutRecorderProps) {
  const [recording, setRecording] = useState(false)

  function handleKeyDown(event: ReactKeyboardEvent<HTMLButtonElement>) {
    // Stop the captured combo from also triggering the live global shortcut.
    event.preventDefault()
    event.stopPropagation()

    if (event.key === 'Escape') {
      setRecording(false)
      return
    }

    const next = eventToAccelerator(event.nativeEvent)
    if (next) {
      onChange(next)
      setRecording(false)
    }
  }

  return (
    <div className="flex items-center gap-1.5">
      <button
        className={`min-w-[112px] rounded-md border px-2.5 py-1 text-center font-mono text-xs transition-colors ${
          recording
            ? 'border-primary bg-primary/10 text-foreground'
            : 'border-border bg-muted text-foreground hover:bg-accent'
        }`}
        onBlur={() => setRecording(false)}
        onClick={() => setRecording(true)}
        onKeyDown={recording ? handleKeyDown : undefined}
        type="button"
      >
        {recording ? 'Press keys…' : accelerator}
      </button>
      {isCustom && (
        <Button onClick={onReset} size="sm" type="button" variant="ghost">
          Reset
        </Button>
      )}
    </div>
  )
}
