import type { Credentials } from './api'

export type FieldErrors = Partial<Record<keyof Credentials, string>>
export function validateCredentials(
  data: Credentials,
  register: boolean,
): FieldErrors {
  const errors: FieldErrors = {}
  if (!/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(data.email) || data.email.length > 320)
    errors.email = 'Enter a valid email address.'
  if (!data.password || data.password.length > 128)
    errors.password = 'Enter your password (up to 128 characters).'
  if (register) {
    if (!data.displayName?.trim() || data.displayName.trim().length > 100)
      errors.displayName = 'Enter your name (up to 100 characters).'
    if (
      data.password.length < 8 ||
      data.password.length > 128 ||
      !/\p{Lu}/u.test(data.password) ||
      !/\p{Ll}/u.test(data.password) ||
      !/\p{Nd}/u.test(data.password) ||
      !/[\p{P}\p{S}]/u.test(data.password)
    )
      errors.password =
        'Use 8–128 characters with uppercase, lowercase, a number, and a symbol.'
  }
  return errors
}
