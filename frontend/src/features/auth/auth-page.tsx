import { useEffect, useRef, useState, type FormEvent } from 'react'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import {
  ArrowRight,
  Check,
  Eye,
  EyeOff,
  LockKeyhole,
} from 'lucide-react'
import { Brand, LogoMark } from '@/components/brand'
import { Button } from '@/components/ui/button'
import { FormField } from '@/components/ui/form-field'
import { ApiError } from '@/lib/api'
import { authApi, type Credentials } from './api'
import { useSessionStore } from './session-store'
import { validateCredentials, type FieldErrors } from './validation'
import { AccountPanel } from './components/account-panel'
import { SignInHelp } from './components/sign-in-help'
import { WorkflowPreview } from './components/workflow-preview'

type Mode = 'sign-in' | 'register'
const copyrightYear = new Date().getFullYear()
function currentMode(): Mode {
  return window.location.pathname === '/register' ? 'register' : 'sign-in'
}

export function AuthPage({ onAuthenticated }: { onAuthenticated?: () => void }) {
  const [mode, setMode] = useState<Mode>(currentMode)
  const [showPassword, setShowPassword] = useState(false)
  const [fields, setFields] = useState<FieldErrors>({})
  const [credentials, setCredentials] = useState<Credentials>({
    email: '',
    password: '',
    displayName: '',
  })
  const form = useRef<HTMLFormElement>(null)
  const heading = useRef<HTMLHeadingElement>(null)
  const tokens = useSessionStore((state) => state.tokens)
  const client = useQueryClient()
  const register = mode === 'register'
  const mutation = useMutation({
    mutationFn: (data: Credentials) =>
      register ? authApi.register(data) : authApi.signIn(data),
    onSuccess: (result) => {
      client.clear()
      useSessionStore.getState().setTokens(result)
      setCredentials({ email: '', password: '', displayName: '' })
      onAuthenticated?.()
    },
    onError: (error) => {
      if (error instanceof ApiError && Object.keys(error.fields).length) {
        setFields(
          Object.fromEntries(
            Object.entries(error.fields).map(([key, messages]) => [
              key,
              messages.join(' '),
            ]),
          ),
        )
        requestAnimationFrame(() =>
          form.current
            ?.querySelector<HTMLInputElement>('[aria-invalid="true"]')
            ?.focus(),
        )
      }
    },
  })
  const resetMutation = mutation.reset

  useEffect(() => {
    const onPopState = () => {
      setMode(currentMode())
      setFields({})
      setShowPassword(false)
      resetMutation()
      setCredentials((previous) => ({ ...previous, password: '' }))
    }
    window.addEventListener('popstate', onPopState)
    return () => window.removeEventListener('popstate', onPopState)
  }, [resetMutation])
  useEffect(() => {
    document.title = tokens
      ? 'Your account · FlowForge'
      : `${register ? 'Create an account' : 'Sign in'} · FlowForge`
  }, [register, tokens])

  function switchMode(next: Mode) {
    window.history.pushState(
      null,
      '',
      next === 'register' ? '/register' : '/login',
    )
    setMode(next)
    setFields({})
    setShowPassword(false)
    mutation.reset()
    setCredentials((previous) => ({ ...previous, password: '' }))
    requestAnimationFrame(() => heading.current?.focus())
  }
  function change(field: keyof Credentials, value: string) {
    setCredentials((previous) => ({ ...previous, [field]: value }))
    setFields((previous) => ({ ...previous, [field]: undefined }))
    mutation.reset()
  }
  function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (mutation.isPending) return
    const data = {
      ...credentials,
      email: credentials.email.trim(),
      displayName: credentials.displayName?.trim(),
    }
    const errors = validateCredentials(data, register)
    setFields(errors)
    if (Object.keys(errors).length) {
      requestAnimationFrame(() =>
        form.current
          ?.querySelector<HTMLInputElement>('[aria-invalid="true"]')
          ?.focus(),
      )
      return
    }
    mutation.mutate(data)
  }

  return (
    <main className="auth-layout">
      <a className="skip-link" href="#auth-content">
        Skip to {tokens ? 'account' : register ? 'registration' : 'sign in'}
      </a>
      <section className="story-panel" aria-label="About FlowForge">
        <header className="story-header">
          <Brand />
          <span className="brand-tagline">MAKE WORK FLOW</span>
        </header>
        <div className="story-content">
          <div className="story-intro">
            <span className="eyebrow">
              <span className="eyebrow-line" />A LITTLE STRUCTURE. A LOT OF
              POSSIBILITY.
            </span>
            <h2>
              Good work
              <br />
              Great{' '}
              <span className="flow-word">
                flow
                <svg
                  viewBox="0 0 170 13"
                  preserveAspectRatio="none"
                  aria-hidden="true"
                >
                  <path d="M3 9C40 2 106 1 166 7" />
                </svg>
              </span>
            </h2>
            <p>
              Your projects, people, and processes.
              <br />
              Finally moving in the same direction.
            </p>
          </div>
          <WorkflowPreview />
        </div>
        <footer className="story-footer">
          <span>Built for the way teams work.</span>
          <span>© {copyrightYear} FlowForge</span>
        </footer>
      </section>
      <section className="auth-panel" aria-label="Account access">
        <header className="auth-header">
          <div className="mobile-brand">
            <Brand />
          </div>
          {!tokens && (
            <p>
              {register ? 'Already have an account?' : 'New to FlowForge?'}{' '}
              <button
                className="text-link"
                onClick={() => switchMode(register ? 'sign-in' : 'register')}
                disabled={mutation.isPending}
              >
                {register ? 'Sign in' : 'Create an account'}
                <ArrowRight size={14} aria-hidden="true" />
              </button>
            </p>
          )}
        </header>
        <div className="auth-content" id="auth-content" tabIndex={-1}>
          {tokens ? (
            <AccountPanel />
          ) : (
            <div className="auth-form-container" key={mode}>
              <div className="form-emblem">
                <LogoMark size={30} />
              </div>
              <span className="eyebrow">
                {register
                  ? 'MAKE ROOM FOR GREAT WORK'
                  : 'YOUR WORK, ALL TOGETHER'}
              </span>
              <h1 ref={heading} tabIndex={-1}>
                {register ? 'Start your next chapter.' : 'Welcome back.'}
              </h1>
              <p className="form-description">
                {register
                  ? 'Create an account. Bring your ideas into flow.'
                  : 'Pick up where you and your team left off.'}
              </p>
              <form
                ref={form}
                onSubmit={submit}
                noValidate
                aria-label={register ? 'Create an account' : 'Sign in'}
                aria-busy={mutation.isPending}
              >
                <fieldset disabled={mutation.isPending}>
                  {register && (
                    <FormField
                      label="Full name"
                      name="displayName"
                      autoComplete="name"
                      required
                      maxLength={100}
                      placeholder="Alex Morgan"
                      value={credentials.displayName}
                      onChange={(event) =>
                        change('displayName', event.target.value)
                      }
                      error={fields.displayName}
                    />
                  )}
                  <FormField
                    label="Email address"
                    name="email"
                    type="email"
                    autoComplete="email"
                    autoCapitalize="none"
                    spellCheck={false}
                    maxLength={320}
                    required
                    placeholder="you@company.com"
                    value={credentials.email}
                    onChange={(event) => change('email', event.target.value)}
                    error={fields.email}
                  />
                  <FormField
                    label="Password"
                    name="password"
                    type={showPassword ? 'text' : 'password'}
                    autoComplete={
                      register ? 'new-password' : 'current-password'
                    }
                    maxLength={128}
                    required
                    placeholder={
                      register
                        ? 'Create a strong password'
                        : 'Enter your password'
                    }
                    value={credentials.password}
                    onChange={(event) => change('password', event.target.value)}
                    error={fields.password}
                    hint={
                      register
                        ? '8+ characters, with uppercase, lowercase, a number, and a symbol.'
                        : undefined
                    }
                    trailing={
                      <button
                        className="password-toggle icon-button"
                        type="button"
                        aria-label={
                          showPassword ? 'Hide password' : 'Show password'
                        }
                        aria-pressed={showPassword}
                        onClick={() => setShowPassword(!showPassword)}
                      >
                        {showPassword ? (
                          <EyeOff size={18} />
                        ) : (
                          <Eye size={18} />
                        )}
                      </button>
                    }
                  />
                  {!register && (
                    <div className="form-options">
                      <span>
                        <LockKeyhole size={12} aria-hidden="true" />
                        Your workspace, securely.
                      </span>
                      <SignInHelp />
                    </div>
                  )}
                  {mutation.isError && (
                    <div className="error-notice" role="alert">
                      {mutation.error instanceof ApiError &&
                      Object.keys(mutation.error.fields).length
                        ? 'Check the highlighted fields and try again.'
                        : mutation.error.message}
                    </div>
                  )}
                  <Button type="submit" loading={mutation.isPending}>
                    {mutation.isPending
                      ? register
                        ? 'Creating your account…'
                        : 'Signing you in…'
                      : register
                        ? 'Create account'
                        : 'Sign in'}
                    {!mutation.isPending && (
                      <ArrowRight size={17} aria-hidden="true" />
                    )}
                  </Button>
                </fieldset>
              </form>
              <div className="form-divider">
                <span />A calmer way to work
                <span />
              </div>
              <div className="auth-benefits">
                <span>
                  <Check size={14} />
                  Keep everyone aligned
                </span>
                <span>
                  <Check size={14} />
                  Automate the everyday
                </span>
              </div>
              <p className="form-switch">
                {register
                  ? 'Already part of a team?'
                  : 'A fresh start for your team?'}{' '}
                <button
                  className="text-link"
                  onClick={() => switchMode(register ? 'sign-in' : 'register')}
                  disabled={mutation.isPending}
                >
                  {register ? 'Sign in' : 'Create an account'}
                </button>
              </p>
            </div>
          )}
        </div>
        <footer className="auth-footer">
          <LockKeyhole size={13} aria-hidden="true" />
          <span>A secure space for your team’s next big thing.</span>
        </footer>
      </section>
    </main>
  )
}
