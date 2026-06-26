/** Application-level providers for routing-independent React infrastructure. */

import { useEffect, type ReactNode } from 'react'

import { TooltipProvider } from '@/components/ui/tooltip'
import { useSettingsStore } from '@/features/settings/store'

type AppProvidersProps = {
  children: ReactNode
}

export function AppProviders({ children }: AppProvidersProps) {
  const theme = useSettingsStore((state) => state.theme)

  useEffect(() => {
    document.documentElement.classList.toggle('dark', theme === 'dark')
  }, [theme])

  return <TooltipProvider>{children}</TooltipProvider>
}
