import type { ButtonHTMLAttributes } from 'react'
import { LoaderCircle } from 'lucide-react'
import { cn } from '@/lib/utils'

type Props = ButtonHTMLAttributes<HTMLButtonElement> & {
  variant?: 'primary' | 'secondary' | 'text'
  loading?: boolean
}

export function Button({
  children,
  className,
  variant = 'primary',
  loading,
  disabled,
  ...props
}: Props) {
  return (
    <button
      className={cn('button', `button--${variant}`, className)}
      disabled={disabled || loading}
      aria-busy={loading || undefined}
      {...props}
    >
      {loading && (
        <LoaderCircle className="spin" size={17} aria-hidden="true" />
      )}
      {children}
    </button>
  )
}
