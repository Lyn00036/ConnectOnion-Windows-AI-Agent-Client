/** Shared button component for consistent command controls across features. */

import type { ButtonHTMLAttributes, ReactNode } from 'react'

type ButtonProps = ButtonHTMLAttributes<HTMLButtonElement> & {
  children: ReactNode
}

export function Button({ children, ...props }: ButtonProps) {
  return <button {...props}>{children}</button>
}
