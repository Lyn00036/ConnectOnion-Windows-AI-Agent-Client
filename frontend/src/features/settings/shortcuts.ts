/** User-customisable keyboard shortcuts (definitions, utils, persisted store). */

import { create } from 'zustand'

import { persistShortcutOverrides } from './preferencesStorage'
/** Stable ids for every rebindable action in the app. */
export type ShortcutId = 'toggle-sidebar' | 'new-chat'

export type ShortcutDef = {
  id: ShortcutId
  label: string
  description: string
  /** Default accelerator string, e.g. "Ctrl+B". */
  defaultAccelerator: string
}

/** Single source of truth for the shortcuts shown in Settings. */
export const SHORTCUT_DEFS: ShortcutDef[] = [
  {
    id: 'toggle-sidebar',
    label: 'Toggle sidebar',
    description: 'Show or hide the left navigation panel.',
    defaultAccelerator: 'Ctrl+B',
  },
  {
    id: 'new-chat',
    label: 'New chat',
    description: 'Start a new conversation with the current agent.',
    defaultAccelerator: 'Ctrl+N',
  },
]

const DEFAULT_ACCELERATORS: Record<ShortcutId, string> = SHORTCUT_DEFS.reduce(
  (map, def) => {
    map[def.id] = def.defaultAccelerator
    return map
  },
  {} as Record<ShortcutId, string>,
)

/**
 * Turn a keyboard event into a normalised accelerator string (or null when only
 * modifier keys are pressed). Ctrl and Meta (Cmd) are unified as "Ctrl" so the
 * same binding works on Windows and macOS.
 */
export function eventToAccelerator(event: KeyboardEvent): string | null {
  const key = event.key
  if (key === 'Control' || key === 'Alt' || key === 'Shift' || key === 'Meta' || key === 'OS') {
    return null
  }

  let mainKey: string
  if (key === ' ' || key === 'Spacebar') {
    mainKey = 'Space'
  } else if (key.length === 1) {
    mainKey = key.toUpperCase()
  } else {
    mainKey = key
  }

  const parts: string[] = []
  if (event.ctrlKey || event.metaKey) parts.push('Ctrl')
  if (event.altKey) parts.push('Alt')
  if (event.shiftKey) parts.push('Shift')
  parts.push(mainKey)
  return parts.join('+')
}

/** True when the event corresponds to the given accelerator string. */
export function matchesAccelerator(event: KeyboardEvent, accelerator: string): boolean {
  const pressed = eventToAccelerator(event)
  return pressed !== null && pressed.toLowerCase() === accelerator.toLowerCase()
}

type ShortcutsState = {  overrides: Partial<Record<ShortcutId, string>>
  /** Resolve the effective accelerator (override or default) for an action. */
  getAccelerator: (id: ShortcutId) => string
  setAccelerator: (id: ShortcutId, accelerator: string) => void
  resetAccelerator: (id: ShortcutId) => void
}

export const useShortcutsStore = create<ShortcutsState>((set, get) => ({
  overrides: {},
  getAccelerator: (id) => get().overrides[id] ?? DEFAULT_ACCELERATORS[id],
  setAccelerator: (id, accelerator) => {
    const next = { ...get().overrides, [id]: accelerator }
    void persistShortcutOverrides(next)
    set({ overrides: next })
  },
  resetAccelerator: (id) => {
    const next = { ...get().overrides }
    delete next[id]
    void persistShortcutOverrides(next)
    set({ overrides: next })
  },
}))

/** React hook returning the live accelerator for an action (re-renders on change). */
export function useShortcutAccelerator(id: ShortcutId): string {
  const overrides = useShortcutsStore((state) => state.overrides)
  return overrides[id] ?? DEFAULT_ACCELERATORS[id]
}
