import { useEffect, useRef, useState, type FormEvent } from 'react'
import { useMutation } from '@tanstack/react-query'
import { Dialog } from '@base-ui/react/dialog'
import { Eye, EyeOff, X } from 'lucide-react'
import { Button } from '@/components/ui/button'
import { FormField } from '@/components/ui/form-field'
import { ApiError } from '@/lib/api'
import { authApi } from '../api'
import { validateCredentials } from '../validation'

export function SignInHelp() {
  const [open, setOpen] = useState(false)
  return (
    <Dialog.Root open={open} onOpenChange={setOpen}>
      <Dialog.Trigger className="text-link">Forgot password?</Dialog.Trigger>
      <Dialog.Portal>
        <Dialog.Backdrop className="dialog-backdrop" />
        <Dialog.Popup className="dialog-popup password-recovery-dialog">
          {open && <PasswordRecovery />}
          <Dialog.Close className="dialog-close icon-button" aria-label="Close password reset">
            <X size={19} aria-hidden="true" />
          </Dialog.Close>
        </Dialog.Popup>
      </Dialog.Portal>
    </Dialog.Root>
  )
}

function PasswordRecovery() {
  const [step, setStep] = useState<'email' | 'code' | 'password' | 'done'>('email')
  const [email, setEmail] = useState('')
  const [code, setCode] = useState('')
  const [password, setPassword] = useState('')
  const [confirmation, setConfirmation] = useState('')
  const [showPassword, setShowPassword] = useState(false)
  const [showConfirmation, setShowConfirmation] = useState(false)
  const [resetToken, setResetToken] = useState('')
  const [error, setError] = useState('')
  const [cooldownUntil, setCooldownUntil] = useState(0)
  const [now, setNow] = useState(Date.now)
  const form = useRef<HTMLFormElement>(null)
  const done = useRef<HTMLHeadingElement>(null)
  const remaining = Math.max(0, Math.ceil((cooldownUntil - now) / 1000))
  useEffect(() => {
    if (!remaining) return
    const timer = window.setInterval(() => setNow(Date.now()), 1000)
    return () => window.clearInterval(timer)
  }, [remaining])
  const send = useMutation({
    mutationFn: () => authApi.forgotPassword(email.trim()),
    onSuccess: () => {
      setCooldownUntil(Date.now() + 60_000)
      setNow(Date.now())
      setCode('')
      setStep('code')
    },
  })
  const verify = useMutation({
    mutationFn: () => authApi.verifyPasswordReset(email.trim(), code),
    onSuccess: (result) => {
      setResetToken(result.resetToken)
      setStep('password')
    },
  })
  const reset = useMutation({
    mutationFn: () => authApi.resetPassword(resetToken, password),
    onSuccess: () => {
      setPassword('')
      setConfirmation('')
      setResetToken('')
      setStep('done')
    },
  })
  const busy = send.isPending || verify.isPending || reset.isPending
  useEffect(() => {
    if (busy) return
    if (step === 'done') done.current?.focus()
    else form.current?.querySelector('input')?.focus()
  }, [step, busy])
  const failure = send.error || verify.error || reset.error
  const field = step === 'email' ? 'email' : step === 'code' ? 'code' : 'password'
  const fieldError = error || (failure instanceof ApiError ? failure.fields[field]?.join(' ') : undefined)

  function clearErrors() {
    setError('')
    send.reset()
    verify.reset()
    reset.reset()
  }
  function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    // Dialog portals retain the React parent tree inside the sign-in form.
    event.stopPropagation()
    clearErrors()
    if (step === 'email') {
      const message = validateCredentials({ email: email.trim(), password: '' }, false).email
      if (message) { setError(message); return }
      send.mutate()
    } else if (step === 'code') {
      if (!/^\d{6}$/.test(code)) { setError('Enter the six-digit code from your email.'); return }
      verify.mutate()
    } else if (step === 'password') {
      const message = validateCredentials({ email, password, displayName: 'Reset' }, true).password
      if (message) { setError(message); return }
      if (password !== confirmation) { setError('Passwords must match.'); return }
      reset.mutate()
    }
  }

  return (
    <>
      <Dialog.Title ref={done} tabIndex={-1}>
        {step === 'done' ? 'Password updated' : 'Reset your password'}
      </Dialog.Title>
      <Dialog.Description>
        {step === 'email' && 'Enter your account email to receive a password reset code.'}
        {step === 'code' && `If an active account exists for ${email.trim()}, a code has been sent. Enter it below. It expires in 10 minutes.`}
        {step === 'password' && 'Email confirmed. Choose a new password within 10 minutes.'}
        {step === 'done' && 'Your password has been updated. Sign in with your new password.'}
      </Dialog.Description>
      {step === 'done' ? (
        <Dialog.Close className="button button--primary">Back to sign in</Dialog.Close>
      ) : (
        <form ref={form} onSubmit={submit} noValidate aria-busy={busy} className="password-recovery-form">
          <fieldset disabled={busy}>
            {step === 'email' && <FormField label="Email address" name="email" type="email" autoComplete="email"
              required maxLength={320} value={email} error={fieldError}
              onChange={(event) => { setEmail(event.target.value); clearErrors() }} />}
            {step === 'code' && <FormField label="Verification code" name="code" inputMode="numeric"
              autoComplete="one-time-code" required maxLength={6} value={code} error={fieldError}
              onChange={(event) => { setCode(event.target.value); clearErrors() }} />}
            {step === 'password' && <>
              <FormField label="New password" name="password" type={showPassword ? 'text' : 'password'} autoComplete="new-password"
                required maxLength={128} value={password} error={fieldError}
                hint="8–128 characters, with uppercase, lowercase, a number, and a symbol."
                trailing={
                  <button className="password-toggle icon-button" type="button"
                    aria-label={showPassword ? 'Hide new password' : 'Show new password'}
                    aria-pressed={showPassword} onClick={() => setShowPassword((visible) => !visible)}>
                    {showPassword ? <EyeOff size={18} aria-hidden="true" /> : <Eye size={18} aria-hidden="true" />}
                  </button>
                }
                onChange={(event) => { setPassword(event.target.value); clearErrors() }} />
              <FormField label="Confirm new password" name="confirmation" type={showConfirmation ? 'text' : 'password'} autoComplete="new-password"
                required maxLength={128} value={confirmation}
                trailing={
                  <button className="password-toggle icon-button" type="button"
                    aria-label={showConfirmation ? 'Hide password confirmation' : 'Show password confirmation'}
                    aria-pressed={showConfirmation} onClick={() => setShowConfirmation((visible) => !visible)}>
                    {showConfirmation ? <EyeOff size={18} aria-hidden="true" /> : <Eye size={18} aria-hidden="true" />}
                  </button>
                }
                onChange={(event) => { setConfirmation(event.target.value); clearErrors() }} />
            </>}
            {fieldError && <span role="alert" className="recovery-error-announcement">{fieldError}</span>}
            {!fieldError && failure && <p role="alert" className="error-notice">{failure.message}</p>}
            <Button type="submit" loading={busy}>
              {step === 'email' ? 'Send reset code' : step === 'code' ? 'Verify code' : 'Update password'}
            </Button>
            {step === 'code' && <Button type="button" variant="text" disabled={remaining > 0}
              onClick={() => { clearErrors(); send.mutate() }}>
              {remaining > 0 ? `Resend code in ${remaining}s` : 'Resend code'}
            </Button>}
            {step !== 'email' && <Button type="button" variant="text" onClick={() => {
              clearErrors(); setResetToken(''); setPassword(''); setConfirmation(''); setShowPassword(false); setShowConfirmation(false); setStep('email')
            }}>Start again</Button>}
          </fieldset>
        </form>
      )}
    </>
  )
}
