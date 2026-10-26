import { useId, type InputHTMLAttributes, type ReactNode } from 'react'
import { useFieldValidation } from './use-field-validation'

type Props = InputHTMLAttributes<HTMLInputElement> & {
  label: string
  error?: string
  hint?: string
  trailing?: ReactNode
}

export function FormField({
  label,
  error: serverError,
  hint,
  trailing,
  id,
  className,
  onChange,
  onInvalid,
  ...props
}: Props) {
  const generatedId = useId()
  const inputId = id ?? generatedId
  const validation = useFieldValidation<HTMLInputElement>(
    label,
    serverError,
    onChange,
    onInvalid,
  )
  const error = validation.error
  return (
    <div className="form-field">
      <label htmlFor={inputId}>{label}</label>
      <div className="input-wrap">
        <input
          {...props}
          onChange={validation.onChange}
          onInvalid={validation.onInvalid}
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
          role={error ? 'alert' : undefined}
        >
          {error || hint}
        </p>
      )}
    </div>
  )
}
