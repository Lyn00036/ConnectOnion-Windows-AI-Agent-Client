/** Desktop settings: appearance now, more preference groups later. */

// TODO(settings-ui): Add further groups as features land:
// - Agent defaults
// - Local data/history
// - Network timeout and retry behavior
// - Secure credential management status

import { Moon, Sun } from 'lucide-react'

import { Button } from '@/components/ui/button'

import { useSettingsStore } from '../store'

export function SettingsPanel() {
  const theme = useSettingsStore((state) => state.theme)
  const setTheme = useSettingsStore((state) => state.setTheme)

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
      </section>
    </div>
  )
}
