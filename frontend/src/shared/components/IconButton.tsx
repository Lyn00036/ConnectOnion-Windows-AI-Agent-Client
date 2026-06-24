/** Accessible shadcn button wrapper for Lucide-only actions. */

import type { ComponentProps } from 'react'

import { Button } from '@/components/ui/button'
import { Tooltip, TooltipContent, TooltipTrigger } from '@/components/ui/tooltip'

type IconButtonProps = Omit<ComponentProps<typeof Button>, 'aria-label'> & {
  label: string
}

export function IconButton({ label, ...props }: IconButtonProps) {
  return (
    <Tooltip>
      <TooltipTrigger render={<Button aria-label={label} {...props} />} />
      <TooltipContent>{label}</TooltipContent>
    </Tooltip>
  )
}
