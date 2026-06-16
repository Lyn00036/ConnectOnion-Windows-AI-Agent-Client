/** Shared input component for simple text fields across feature forms. */

import type { InputHTMLAttributes } from 'react'

type InputProps = InputHTMLAttributes<HTMLInputElement>

export function Input(props: InputProps) {
  return <input {...props} />
}
