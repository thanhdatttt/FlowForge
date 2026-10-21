import { useEffect, useState, type FormEvent } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import {
  ArrowRight,
  Check,
  CircleCheck,
  LogOut,
  Mail,
  ShieldCheck,
} from 'lucide-react'
import { Button } from '@/components/ui/button'
import { FormField } from '@/components/ui/form-field'
import { authApi } from '../api'
import { useSessionStore } from '../session-store'

export function AccountPanel() {
  const client = useQueryClient()
  const [code, setCode] = useState('')
  const [codeError, setCodeError] = useState('')
  const [cooldownUntil, setCooldownUntil] = useState(0)
  const [now, setNow] = useState(Date.now)
  const user = useQuery({
    queryKey: ['auth', 'me'],
    queryFn: ({ signal }) => authApi.me(signal),
    retry: false,
  })
  const send = useMutation({
    mutationFn: authApi.requestCode,
    onSuccess: () => {
      setCooldownUntil(Date.now() + 60_000)
      setNow(Date.now())
    },
  })
  const confirm = useMutation({
    mutationFn: authApi.confirmCode,
    onSuccess: async () => {
      await client.invalidateQueries({ queryKey: ['auth', 'me'] })
    },
  })
  const logout = useMutation({
    mutationFn: async () => {
      // Cancel user reads before revoking the latest rotated refresh token.
      await client.cancelQueries({ queryKey: ['auth'] })
      const tokens = useSessionStore.getState().tokens
      if (tokens) await authApi.signOut(tokens.refreshToken)
    },
    onSuccess: () => {
      useSessionStore.getState().clear()
      client.clear()
    },
  })
  const remaining = Math.max(0, Math.ceil((cooldownUntil - now) / 1000))
  useEffect(() => {
    if (!remaining) return
    const interval = window.setInterval(() => setNow(Date.now()), 1000)
    return () => window.clearInterval(interval)
  }, [remaining])

  function verify(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (!/^\d{6}$/.test(code)) {
      setCodeError('Enter the six-digit code from your email.')
      return
    }
    setCodeError('')
    confirm.mutate(code)
  }

  if (user.isPending)
    return (
      <div className="account-panel" role="status">
        <div className="skeleton skeleton-icon" />
        <div className="skeleton skeleton-title" />
        <div className="skeleton skeleton-copy" />
        <p className="muted">Getting your account ready…</p>
      </div>
    )
  if (user.isError)
    return (
      <div className="account-panel">
        <h1>Let’s try that again</h1>
        <p className="error-notice" role="alert">
          {user.error.message}
        </p>
        <Button onClick={() => void user.refetch()} loading={user.isFetching}>
          Try again
        </Button>
        <Button
          variant="text"
          className="button--logout"
          onClick={() => logout.mutate()}
          loading={logout.isPending}
        >
          Sign out
        </Button>
        {logout.isError && (
          <p className="error-notice" role="alert">
            {logout.error.message}
          </p>
        )}
      </div>
    )
  const verified = user.data.emailVerified
  const busy = send.isPending || confirm.isPending || logout.isPending
  return (
    <section className="account-panel" aria-labelledby="account-title">
      <div className={`form-emblem ${verified ? 'form-emblem--success' : ''}`}>
        {verified ? <ShieldCheck size={25} /> : <Mail size={25} />}
      </div>
      <span className="eyebrow">
        {verified ? 'YOU’RE ALL SET' : 'ONE MORE THING'}
      </span>
      <h1 id="account-title">
        {verified
          ? `Welcome, ${user.data.displayName}.`
          : 'Make it official.'}
      </h1>
      <p className="form-description">
        {verified ? (
          'You’re signed in to FlowForge. Your next great project starts here.'
        ) : (
          <>
            Verify <strong>{user.data.email}</strong> to accept workspace
            invitations and keep your team connected.
          </>
        )}
      </p>
      {verified ? (
        <div className="account-ready" role="status">
          <div className="account-ready-heading">
            <CircleCheck size={20} aria-hidden="true" />
            <strong>Your account is ready</strong>
          </div>
          <p>Open your workspaces from the sidebar.</p>
        </div>
      ) : (
        <>
          {!send.isSuccess ? (
            <Button
              onClick={() => send.mutate()}
              loading={send.isPending}
              disabled={busy}
            >
              Send verification code <ArrowRight size={17} />
            </Button>
          ) : (
            <>
              <p className="success-notice" role="status">
                <Check size={16} aria-hidden="true" />
                <span>Code sent. Check your inbox, including spam.</span>
              </p>
              <form onSubmit={verify} noValidate>
                <FormField
                  label="Verification code"
                  name="code"
                  value={code}
                  onChange={(event) => {
                    setCode(event.target.value.replace(/\D/g, '').slice(0, 6))
                    setCodeError('')
                    confirm.reset()
                  }}
                  inputMode="numeric"
                  autoComplete="one-time-code"
                  maxLength={6}
                  required
                  placeholder="000000"
                  className="code-input"
                  error={codeError}
                  hint="Use the latest code. Older codes no longer work."
                />
                <Button
                  type="submit"
                  loading={confirm.isPending}
                  disabled={busy}
                >
                  Verify email <ArrowRight size={17} />
                </Button>
              </form>
              <Button
                variant="text"
                onClick={() => send.mutate()}
                disabled={remaining > 0 || busy}
                loading={send.isPending}
              >
                {remaining > 0 ? `Resend code in ${remaining}s` : 'Resend code'}
              </Button>
            </>
          )}
          {(send.isError || confirm.isError) && (
            <p className="error-notice" role="alert">
              {send.error?.message || confirm.error?.message}
            </p>
          )}
          <p className="field-hint verification-note">
            Email verification is only needed once. You can sign out and finish
            later.
          </p>
        </>
      )}
      <div className="signed-in-details">
        <span className="account-avatar">
          {user.data.displayName.slice(0, 1).toUpperCase()}
        </span>
        <div>
          <strong>{user.data.displayName}</strong>
          <span>{user.data.email}</span>
        </div>
        <Button
          variant="text"
          className="button--logout"
          aria-label="Sign out"
          onClick={() => logout.mutate()}
          loading={logout.isPending}
          disabled={busy}
        >
          <LogOut size={18} />
        </Button>
      </div>
      {logout.isError && (
        <p className="error-notice" role="alert">
          {logout.error.message} Your session is still active; try signing out
          again.
        </p>
      )}
    </section>
  )
}
