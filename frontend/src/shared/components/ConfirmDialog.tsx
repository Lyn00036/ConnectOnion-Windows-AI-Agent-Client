/** Non-blocking React confirmation dialog — a drop-in replacement for window.confirm(). */

import { useEffect, useRef } from 'react'

import { Button } from '@/components/ui/button'

export type ConfirmDialogProps = {
  cancelLabel?: string
  children?: React.ReactNode
  confirmLabel?: string
  destructive?: boolean
  onCancel: () => void
  onConfirm: () => void
  open: boolean
  title: string
}

export function ConfirmDialog({
  cancelLabel = 'Cancel',
  children,
  confirmLabel = 'OK',
  destructive = false,
  onCancel,
  onConfirm,
  open,
  title,
}: ConfirmDialogProps) {
  const cancelRef = useRef<HTMLButtonElement>(null)

  useEffect(() => {
    if (open) {
      cancelRef.current?.focus()
    }
  }, [open])

  useEffect(() => {
    if (!open) return

    function handleKeyDown(event: KeyboardEvent) {
      if (event.key === 'Escape') {
        event.preventDefault()
        onCancel()
      }
    }

    document.addEventListener('keydown', handleKeyDown)
    return () => document.removeEventListener('keydown', handleKeyDown)
  }, [open, onCancel])

  if (!open) return null

  return (
    <div
      aria-labelledby="confirm-dialog-title"
      aria-modal="true"
      className="fixed inset-0 z-50 flex items-center justify-center"
      role="alertdialog"
    >
      {/* Backdrop */}
      <div
        aria-hidden="true"
        className="absolute inset-0 bg-black/40"
        onClick={onCancel}
      />

      {/* Dialog panel */}
      <div className="relative z-10 mx-4 w-full max-w-sm rounded-2xl border border-border bg-card p-6 shadow-2xl">
        <h2
          className="mb-2 text-[15px] font-bold text-foreground"
          id="confirm-dialog-title"
        >
          {title}
        </h2>

        {children ? (
          <p className="mb-5 text-[13px] leading-relaxed text-muted-foreground">
            {children}
          </p>
        ) : null}

        <div className="flex justify-end gap-3">
          <Button
            className="min-h-[34px] text-[13px] font-semibold"
            onClick={onCancel}
            ref={cancelRef}
            size="sm"
            type="button"
            variant="outline"
          >
            {cancelLabel}
          </Button>

          <Button
            className="min-h-[34px] text-[13px] font-semibold"
            onClick={onConfirm}
            size="sm"
            type="button"
            variant={destructive ? 'destructive' : 'default'}
          >
            {confirmLabel}
          </Button>
        </div>
      </div>
    </div>
  )
}
