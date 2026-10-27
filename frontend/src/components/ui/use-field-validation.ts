import { useState, type ChangeEventHandler, type FormEventHandler } from 'react'

type Field = HTMLInputElement | HTMLTextAreaElement | HTMLSelectElement

// Keep native validity rules; replace browser tooltips with an associated inline message.
export function useFieldValidation<T extends Field>(
  label: string,
  error: string | undefined,
  onChange?: ChangeEventHandler<T>,
  onInvalid?: FormEventHandler<T>,
) {
  const [nativeError, setNativeError] = useState('')
  const message = (field: T) =>
    field.validity.valueMissing ||
    (!field.validity.valid && field.required && !field.value.trim())
      ? field.type === 'file'
        ? 'Choose a file to upload.'
        : `${label} is required.`
      : field.validationMessage
  return {
    error: error || nativeError,
    onInvalid: ((event) => {
      onInvalid?.(event)
      if (event.defaultPrevented) return
      event.preventDefault()
      setNativeError(message(event.currentTarget))
      const first = event.currentTarget.form?.querySelector(
        'input:invalid, select:invalid, textarea:invalid',
      )
      if (first === event.currentTarget) event.currentTarget.focus()
    }) satisfies FormEventHandler<T>,
    onChange: ((event) => {
      if (nativeError) setNativeError(message(event.currentTarget))
      onChange?.(event)
    }) satisfies ChangeEventHandler<T>,
  }
}
