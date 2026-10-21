import { useState } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { Button } from '@/components/ui/button'
import { FormField } from '@/components/ui/form-field'
import type { CurrentUser } from '@/features/auth/api'
import { invitationApi } from './api'
import { parseInvitationLink, type InvitationLink } from './rules'
import { ActionForm, QueryState } from './shared'
import { text } from './rules'

export function InvitationEntry({
  open,
}: {
  open: (invitation: InvitationLink) => void
}) {
  const [error, setError] = useState('')
  return (
    <section className="workspace-card workspace-join">
      <h2>Join a workspace</h2>
      <p className="muted">
        Paste the invitation link shared by your team or received by email.
      </p>
      <form
        className="workspace-form"
        onSubmit={(event) => {
          event.preventDefault()
          const link = parseInvitationLink(
            text(new FormData(event.currentTarget), 'link'),
          )
          if (!link) {
            setError(
              'Invalid invitation link.',
            )
            return
          }
          setError('')
          event.currentTarget.reset()
          open(link)
        }}
      >
        <FormField
          name="link"
          label="Invitation link"
          type="text"
          required
          autoComplete="off"
          error={error}
        />
        <Button type="submit">Review invitation</Button>
      </form>
    </section>
  )
}
export function InvitationPage({
  invitation,
  user,
  verify,
  finish,
}: {
  invitation: InvitationLink
  user: CurrentUser
  verify: () => void
  finish: (workspaceId?: string) => void
}) {
  const client = useQueryClient()
  const details = useQuery({
    queryKey: ['phase2', 'invitation', invitation.id],
    queryFn: ({ signal }) => invitationApi.get(invitation.id, signal),
    retry: false,
  })
  const [now] = useState(Date.now)
  const [result, setResult] = useState<'accepted' | 'rejected' | null>(null)
  return (
    <section className="workspace-card workspace-invitation">
      <span className="eyebrow">YOUR TEAM IS WAITING</span>
      <h2>Workspace invitation</h2>
      <QueryState
        pending={details.isPending}
        error={details.error}
        retry={details.refetch}
      >
        {details.data &&
          (result ? (
            <>
              <p role="status">Invitation {result}.</p>
              <Button
                onClick={() =>
                  finish(
                    result === 'accepted'
                      ? details.data?.workspaceId
                      : undefined,
                  )
                }
              >
                {result === 'accepted'
                  ? 'Open workspace'
                  : 'Back to workspaces'}
              </Button>
            </>
          ) : (
            <>
              <p>
                Invitation for <strong>{details.data.email}</strong> as{' '}
                <strong>{details.data.role}</strong>.
              </p>
              <p className="muted">
                Signed in as {user.email}. Expires{' '}
                {new Date(details.data.expiresAt).toLocaleString()}.
              </p>
              {details.data.status !== 'Pending' ||
              Date.parse(details.data.expiresAt) <= now ? (
                <p role="status">
                  This invitation is{' '}
                  {details.data.status === 'Pending'
                    ? 'expired'
                    : details.data.status.toLowerCase()}
                  . Ask your team for a new invitation.
                </p>
              ) : !user.emailVerified ? (
                <>
                  <p>
                    Verify your email before accepting or declining this
                    invitation.
                  </p>
                  <Button onClick={verify}>Verify email</Button>
                </>
              ) : (
                <ActionForm
                  label="Confirm response"
                  submit={async (data) => {
                    const accept = text(data, 'response') === 'accept'
                    await invitationApi.respond(invitation.token, accept)
                    // Tokens are kept only in component memory, never in query keys or cache.
                    await client.invalidateQueries({ queryKey: ['phase2'] })
                    setResult(accept ? 'accepted' : 'rejected')
                  }}
                >
                  {() => (
                    <fieldset className="workspace-response">
                      <legend>Your response</legend>
                      <label>
                        <input
                          type="radio"
                          name="response"
                          value="accept"
                          defaultChecked
                        />{' '}
                        Accept invitation
                      </label>
                      <label>
                        <input type="radio" name="response" value="reject" />{' '}
                        Decline invitation
                      </label>
                    </fieldset>
                  )}
                </ActionForm>
              )}
            </>
          ))}
      </QueryState>
      {!result && (
        <Button variant="text" onClick={() => finish()}>
          Back to workspaces
        </Button>
      )}
    </section>
  )
}
