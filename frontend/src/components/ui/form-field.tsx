import { useId, type InputHTMLAttributes, type ReactNode } from 'react'

type Props = InputHTMLAttributes<HTMLInputElement> & {
  label: string
  error?: string
  hint?: string
  trailing?: ReactNode
}

export function FormField({
  label,
  error,
  hint,
  trailing,
  id,
  className,
  ...props
}: Props) {
  const generatedId = useId()
  const inputId = id ?? generatedId
  return (
    <div className="form-field">
      <label htmlFor={inputId}>{label}</label>
      <div className="input-wrap">
        <input
          {...props}
          id={inputId}
          className={className}
          aria-invalid={!!error}
          aria-describedby={
            error || hint ? `${inputId}-description` : undefined
          }
        />
        {trailing}
      </div>
      {(error || hint) && (
        <p
          id={`${inputId}-description`}
          className={error ? 'field-error' : 'field-hint'}
        >
          {error || hint}
        </p>
      )}
    </div>
  )
}
