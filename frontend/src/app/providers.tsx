/** Application-level providers for routing-independent React infrastructure. */

import { useEffect, type ReactNode } from 'react'

import { TooltipProvider } from '@/components/ui/tooltip'
import { useAgentStore } from '@/features/agents/store'
import { useSessionStore } from '@/features/sessions/store'
import { MESSAGE_FONT_SIZES, useSettingsStore } from '@/features/settings/store'

type AppProvidersProps = {
  children: ReactNode
}

export function AppProviders({ children }: AppProvidersProps) {
  const theme = useSettingsStore((state) => state.theme)
  const messageFontSize = useSettingsStore((state) => state.messageFontSize)
  const hydrateAgents = useAgentStore((state) => state.hydrate)
  const hydrateSessions = useSessionStore((state) => state.hydrate)
  const hydrateSettings = useSettingsStore((state) => state.hydrate)

  useEffect(() => {
    void Promise.all([hydrateAgents(), hydrateSessions(), hydrateSettings()])
  }, [hydrateAgents, hydrateSessions, hydrateSettings])

  useEffect(() => {
    document.documentElement.classList.toggle('dark', theme === 'dark')
  }, [theme])

  useEffect(() => {
    document.documentElement.style.setProperty('--co-msg-size', MESSAGE_FONT_SIZES[messageFontSize])
  }, [messageFontSize])

  return <TooltipProvider>{children}</TooltipProvider>
}
