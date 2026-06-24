/** Shared empty state component for placeholder desktop pages. */

type EmptyStateProps = {
  title: string
  description: string
}

export function EmptyState({ title, description }: EmptyStateProps) {
  return (
    <div className="flex min-h-80 flex-col items-center justify-center gap-2 p-8 text-center">
      <h2 className="m-0 text-lg font-semibold text-[#172033]">{title}</h2>
      <p className="m-0 max-w-[520px] text-sm leading-6 text-[#667085]">{description}</p>
    </div>
  )
}
