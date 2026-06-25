import { useEffect, useState } from 'react'
import { ArrowLeft, ArrowRight, Copy, Minus, Square, X } from 'lucide-react'
import { useNavigate } from 'react-router-dom'

import { Button } from '@/components/ui/button'
import { IconButton } from '@/shared/components/IconButton'
import type { DesktopWindowState } from '@/shared/desktopWindow'

import onionLogo from '../assets/icon.png'

const menuItems = ['File', 'Edit', 'View', 'Help'] as const

export function DesktopTitleBar() {
  const navigate = useNavigate()
  const [isMaximized, setIsMaximized] = useState(false)

  useEffect(() => {
    let isMounted = true
    const desktopWindow = window.desktopWindow

    void desktopWindow?.getState()
      .then((state) => {
        if (isMounted) setIsMaximized(state.isMaximized)
      })
      .catch(() => undefined)

    const removeStateListener = desktopWindow?.onStateChange((state: DesktopWindowState) => {
      setIsMaximized(state.isMaximized)
    })

    return () => {
      isMounted = false
      removeStateListener?.()
    }
  }, [])

  function handleBack() {
    if (window.history.length > 1) {
      navigate(-1)
    }
  }

  function handleForward() {
    navigate(1)
  }

  function handleMinimize() {
    void window.desktopWindow?.minimize()
  }

  function handleToggleMaximize() {
    void window.desktopWindow?.toggleMaximize()
      .then((state) => setIsMaximized(state.isMaximized))
      .catch(() => undefined)
  }

  function handleClose() {
    void window.desktopWindow?.close()
  }

  return (
    <header className="desktop-titlebar grid h-9 shrink-0 grid-cols-[1fr_auto] items-center border-b border-titlebar-border bg-titlebar text-titlebar-foreground">
      <div className="flex min-w-0 items-center gap-1 pl-3">
        <span className="mr-1 grid size-4 place-items-center overflow-hidden rounded-[3px] border border-titlebar-border bg-background">
          <img alt="" className="size-3 object-contain opacity-95" src={onionLogo} />
        </span>
        <div className="desktop-titlebar-controls flex items-center gap-0.5">
          <IconButton className="size-7 min-h-0 rounded-none p-0 text-titlebar-foreground hover:bg-titlebar-accent hover:text-titlebar-accent-foreground" label="Back" onClick={handleBack} size="icon-xs" type="button" variant="ghost">
            <ArrowLeft aria-hidden="true" className="size-3.5" />
          </IconButton>
          <IconButton className="size-7 min-h-0 rounded-none p-0 text-titlebar-muted-foreground hover:bg-titlebar-accent hover:text-titlebar-accent-foreground" label="Forward" onClick={handleForward} size="icon-xs" type="button" variant="ghost">
            <ArrowRight aria-hidden="true" className="size-3.5" />
          </IconButton>
        </div>
        <nav className="desktop-titlebar-controls ml-3 flex h-full items-center" aria-label="Application menu">
          {menuItems.map((item) => (
            <Button className="h-9 rounded-none px-3 text-[12px] font-medium text-titlebar-foreground hover:bg-titlebar-accent hover:text-titlebar-accent-foreground" key={item} type="button" variant="ghost">
              {item}
            </Button>
          ))}
        </nav>
      </div>

      <div className="desktop-titlebar-controls flex h-full items-center justify-end">
        <IconButton className="h-9 w-11 min-h-0 rounded-none p-0 text-titlebar-foreground hover:bg-titlebar-accent hover:text-titlebar-accent-foreground" label="Minimize" onClick={handleMinimize} size="icon-xs" type="button" variant="ghost">
          <Minus aria-hidden="true" className="size-3.5" />
        </IconButton>
        <IconButton className="h-9 w-11 min-h-0 rounded-none p-0 text-titlebar-foreground hover:bg-titlebar-accent hover:text-titlebar-accent-foreground" label={isMaximized ? 'Restore' : 'Maximize'} onClick={handleToggleMaximize} size="icon-xs" type="button" variant="ghost">
          {isMaximized ? <Copy aria-hidden="true" className="size-3" /> : <Square aria-hidden="true" className="size-3" />}
        </IconButton>
        <IconButton className="h-9 w-11 min-h-0 rounded-none p-0 text-titlebar-foreground hover:bg-titlebar-close-hover hover:text-titlebar-close-hover-foreground" label="Close" onClick={handleClose} size="icon-xs" type="button" variant="ghost">
          <X aria-hidden="true" className="size-4" />
        </IconButton>
      </div>
    </header>
  )
}
