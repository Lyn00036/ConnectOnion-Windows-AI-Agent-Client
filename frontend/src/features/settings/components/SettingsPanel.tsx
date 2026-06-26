/** Desktop settings: appearance now, more preference groups later. */

// TODO(settings-ui): Add further groups as features land:
// - Agent defaults
// - Local data/history
// - Network timeout and retry behavior
// - Secure credential management status

import { Moon, Sun } from 'lucide-react'

import { Button } from '@/components/ui/button'

import { useSettingsStore, type MessageFontSize } from '../store'
import { SHORTCUT_DEFS, useShortcutsStore } from '../shortcuts'
import { ShortcutRecorder } from './ShortcutRecorder'

const FONT_SIZE_OPTIONS: { label: string; value: MessageFontSize }[] = [
  { label: 'Small', value: 'sm' },
  { label: 'Medium', value: 'md' },
  { label: 'Large', value: 'lg' },
]

export function SettingsPanel() {
  const theme = useSettingsStore((state) => state.theme)
  const setTheme = useSettingsStore((state) => state.setTheme)
  const messageFontSize = useSettingsStore((state) => state.messageFontSize)
  const setMessageFontSize = useSettingsStore((state) => state.setMessageFontSize)
  const overrides = useShortcutsStore((state) => state.overrides)
  const setAccelerator = useShortcutsStore((state) => state.setAccelerator)
  const resetAccelerator = useShortcutsStore((state) => state.resetAccelerator)

  return (
    <div className="mx-auto w-full max-w-2xl px-6 py-8">
      <h1 className="text-xl font-bold text-foreground">Settings</h1>
      <p className="mt-1 text-sm text-muted-foreground">
        Desktop preferences for this app.
      </p>

      <section className="mt-6 rounded-xl border border-border bg-card p-5 text-card-foreground">
        <h2 className="text-sm font-semibold text-foreground">Appearance</h2>
        <p className="mt-1 text-xs text-muted-foreground">
          Choose how the desktop client looks.
        </p>

        <div className="mt-4 flex items-center justify-between gap-4">
          <div className="min-w-0">
            <p className="text-sm font-medium text-foreground">Theme</p>
            <p className="text-xs text-muted-foreground">
              Switch between light and dark mode.
            </p>
          </div>

          <div className="flex items-center gap-1 rounded-lg border border-border p-1">
            <Button
              onClick={() => setTheme('light')}
              size="sm"
              type="button"
              variant={theme === 'light' ? 'default' : 'ghost'}
            >
              <Sun aria-hidden="true" /> Light
            </Button>
            <Button
              onClick={() => setTheme('dark')}
              size="sm"
              type="button"
              variant={theme === 'dark' ? 'default' : 'ghost'}
            >
              <Moon aria-hidden="true" /> Dark
            </Button>
          </div>
        </div>

        <div className="mt-4 flex items-center justify-between gap-4 border-t border-border pt-4">
          <div className="min-w-0">
            <p className="text-sm font-medium text-foreground">Message text size</p>
            <p className="text-xs text-muted-foreground">
              Adjust how large chat messages appear.
            </p>
          </div>

          <div className="flex items-center gap-1 rounded-lg border border-border p-1">
            {FONT_SIZE_OPTIONS.map((option) => (
              <Button
                key={option.value}
                onClick={() => setMessageFontSize(option.value)}
                size="sm"
                type="button"
                variant={messageFontSize === option.value ? 'default' : 'ghost'}
              >
                {option.label}
              </Button>
            ))}
          </div>
        </div>
      </section>

      <section className="mt-6 rounded-xl border border-border bg-card p-5 text-card-foreground">
        <h2 className="text-sm font-semibold text-foreground">Keyboard shortcuts</h2>
        <p className="mt-1 text-xs text-muted-foreground">
          Click a shortcut, then press the keys you want. Press Esc to cancel.
        </p>

        <ul className="mt-4 divide-y divide-border">
          {SHORTCUT_DEFS.map((def) => {
            const accelerator = overrides[def.id] ?? def.defaultAccelerator
            return (
              <li className="flex items-center justify-between gap-4 py-3 first:pt-0 last:pb-0" key={def.id}>
                <div className="min-w-0">
                  <p className="text-sm font-medium text-foreground">{def.label}</p>
                  <p className="text-xs text-muted-foreground">{def.description}</p>
                </div>
                <ShortcutRecorder
                  accelerator={accelerator}
                  isCustom={overrides[def.id] !== undefined}
                  onChange={(next) => setAccelerator(def.id, next)}
                  onReset={() => resetAccelerator(def.id)}
                />
              </li>
            )
          })}
        </ul>
      </section>
    </div>
  )
}
