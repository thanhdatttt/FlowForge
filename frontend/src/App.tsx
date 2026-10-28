import { useEffect, useState } from 'react'
import { useQueryClient } from '@tanstack/react-query'
import { AuthPage } from '@/features/auth/auth-page'
import { useSessionStore } from '@/features/auth/session-store'
import { WorkspaceApp } from '@/features/workspaces/workspace-app'
import { restoreSession } from '@/lib/api'
import { Button } from '@/components/ui/button'
import {
  parseInvitationLink,
  type InvitationLink,
} from '@/features/workspaces/rules'

export default function App() {
  const [restoring, setRestoring] = useState(true)
  const [restoreError, setRestoreError] = useState<Error | null>(null)
  function restore() {
    void restoreSession()
      .then(() => setRestoring(false))
      .catch((error: Error) => setRestoreError(error))
  }
  useEffect(() => {
    restore()
  }, [])
  const tokens = useSessionStore((state) => state.tokens)
  const client = useQueryClient()
  const authenticated = !!tokens
  useEffect(() => {
    if (!authenticated) {
      void client.cancelQueries()
      client.clear()
    }
  }, [authenticated, client])
  const [path, setPath] = useState(window.location.pathname)
  const [invitation, setInvitation] = useState<InvitationLink | null>(() =>
    parseInvitationLink(window.location.href),
  )
  useEffect(() => {
    const sync = () => {
      const next = parseInvitationLink(window.location.href)
      if (next) {
        setInvitation(next)
        window.history.replaceState(null, '', window.location.pathname)
      }
      if (window.location.pathname.startsWith('/workspaces'))
        setInvitation(null)
      setPath(window.location.pathname)
    }
    // Remove the secret from the active history entry as soon as it is captured.
    sync()
    window.addEventListener('popstate', sync)
    window.addEventListener('hashchange', sync)
    return () => {
      window.removeEventListener('popstate', sync)
      window.removeEventListener('hashchange', sync)
    }
  }, [])
  function navigate(next: string) {
    if (!next.startsWith('/invitations/')) setInvitation(null)
    if (next !== window.location.pathname + window.location.hash)
      window.history.pushState(null, '', next)
    setPath(next.split('#')[0])
    requestAnimationFrame(() => {
      const comments = next.endsWith('#work-comments')
        ? document.getElementById('work-comments')
        : null
      if (comments) {
        comments.scrollIntoView({ block: 'start' })
        comments.focus({ preventScroll: true })
      } else document.getElementById('workspace-content')?.focus()
    })
  }
  if (restoring)
    return (
      <main className="workspace-card" aria-busy={!restoreError}>
        {restoreError ? (
          <>
            <p role="alert">{restoreError.message}</p>
            <Button onClick={() => {
              setRestoreError(null)
              restore()
            }}>
              Try again
            </Button>
          </>
        ) : (
          <p role="status">Restoring your session…</p>
        )}
      </main>
    )
  if (!tokens)
    return (
      <>
        <AuthPage
          onAuthenticated={() => {
            if (invitation) navigate(`/invitations/${invitation.id}`)
            else if (!path.startsWith('/workspaces/')) navigate('/workspaces')
          }}
        />
        {invitation && (
          <div className="invitation-auth-notice" role="status">
            Sign in or create an account with the invited email to continue.
            Your invitation is kept for this tab until you reload.
          </div>
        )}
      </>
    )
  return (
    <WorkspaceApp
      path={path}
      navigate={navigate}
      invitation={invitation}
      clearInvitation={() => setInvitation(null)}
      openInvitation={(next) => {
        setInvitation(next)
        navigate(`/invitations/${next.id}`)
      }}
    />
  )
}
