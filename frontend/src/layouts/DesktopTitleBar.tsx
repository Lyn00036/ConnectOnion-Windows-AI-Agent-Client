import { useEffect, useState } from 'react'
import { Menu } from '@base-ui/react/menu'
import { ArrowLeft, ArrowRight, Check, Copy, Minus, Square, X } from 'lucide-react'
import { useNavigate } from 'react-router-dom'

import { Button } from '@/components/ui/button'
import { IconButton } from '@/shared/components/IconButton'
import type { DesktopWindowState } from '@/shared/desktopWindow'
import { useSettingsStore } from '@/features/settings/store'
import { useShortcutAccelerator } from '@/features/settings/shortcuts'

import onionLogo from '../assets/icon.png'

const menuButtonClass =
  'h-9 rounded-none px-3 text-[12px] font-medium text-titlebar-foreground hover:bg-titlebar-accent hover:text-titlebar-accent-foreground aria-expanded:bg-titlebar-accent aria-expanded:text-titlebar-accent-foreground'

type DesktopTitleBarProps = {
  onToggleSidebar: () => void
  sidebarVisible: boolean
}

export function DesktopTitleBar({ onToggleSidebar, sidebarVisible }: DesktopTitleBarProps) {
  const navigate = useNavigate()
  const [isMaximized, setIsMaximized] = useState(false)
  const theme = useSettingsStore((state) => state.theme)
  const setTheme = useSettingsStore((state) => state.setTheme)
  const toggleSidebarAccelerator = useShortcutAccelerator('toggle-sidebar')

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
          <Button className={menuButtonClass} type="button" variant="ghost">
            File
          </Button>
          <Button className={menuButtonClass} type="button" variant="ghost">
            Edit
          </Button>
          <Menu.Root>
            <Menu.Trigger render={<Button className={menuButtonClass} type="button" variant="ghost">View</Button>} />
            <Menu.Portal>
              <Menu.Positioner align="start" className="z-50" side="bottom" sideOffset={4}>
                <Menu.Popup className="min-w-44 rounded-lg border border-border bg-popover p-1 text-popover-foreground shadow-md outline-none">
                  <Menu.CheckboxItem
                    checked={theme === 'dark'}
                    className="relative flex w-full cursor-default items-center gap-2 rounded-md py-1.5 pr-8 pl-2.5 text-[13px] outline-none select-none data-highlighted:bg-accent data-highlighted:text-accent-foreground"
                    closeOnClick={false}
                    onCheckedChange={(checked) => setTheme(checked ? 'dark' : 'light')}
                  >
                    Dark mode
                    <Menu.CheckboxItemIndicator className="absolute right-2 flex items-center">
                      <Check aria-hidden="true" className="size-4" />
                    </Menu.CheckboxItemIndicator>
                  </Menu.CheckboxItem>
                  <div className="my-1 h-px bg-border" />
                  <Menu.CheckboxItem
                    checked={sidebarVisible}
                    className="relative flex w-full cursor-default items-center gap-2 rounded-md py-1.5 pr-8 pl-2.5 text-[13px] outline-none select-none data-highlighted:bg-accent data-highlighted:text-accent-foreground"
                    closeOnClick={false}
                    onCheckedChange={() => onToggleSidebar()}
                  >
                    Toggle sidebar
                    <span className="ml-auto text-[11px] text-muted-foreground">{toggleSidebarAccelerator}</span>
                    <Menu.CheckboxItemIndicator className="absolute right-2 flex items-center">
                      <Check aria-hidden="true" className="size-4" />
                    </Menu.CheckboxItemIndicator>
                  </Menu.CheckboxItem>
                </Menu.Popup>
              </Menu.Positioner>
            </Menu.Portal>
          </Menu.Root>
          <Menu.Root>
            <Menu.Trigger render={<Button className={menuButtonClass} type="button" variant="ghost">Help</Button>} />
            <Menu.Portal>
              <Menu.Positioner align="start" className="z-50" side="bottom" sideOffset={4}>
                <Menu.Popup className="min-w-44 rounded-lg border border-border bg-popover p-1 text-popover-foreground shadow-md outline-none">
                  <Menu.Item
                    className="relative flex w-full cursor-default items-center gap-2 rounded-md px-2.5 py-1.5 text-[13px] outline-none select-none data-highlighted:bg-accent data-highlighted:text-accent-foreground"
                    onClick={() => navigate('/settings')}
                  >
                    Keyboard shortcuts
                  </Menu.Item>
                </Menu.Popup>
              </Menu.Positioner>
            </Menu.Portal>
          </Menu.Root>
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
