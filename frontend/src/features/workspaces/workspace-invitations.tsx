import { useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { Button } from '@/components/ui/button'
import { FormField } from '@/components/ui/form-field'
import { workspaceApi, type Role, type Workspace } from './api'
import { assignableRoles, frontendInvitationLink } from './rules'
import {
  ActionForm,
  ConfirmAction,
  Empty,
  Modal,
  Pager,
  QueryState,
  SelectField,
} from './shared'
import { text } from './rules'

function InviteForm({ workspace }: { workspace: Workspace }) {
  const [link, setLink] = useState('')
  const [copyMessage, setCopyMessage] = useState('')
  if (link)
    return (
      <div className="workspace-stack">
        <p role="status">
          Invitation created. Copy this link now; it cannot be retrieved later.
        </p>
        <FormField
          label="Invitation link"
          value={link}
          readOnly
          onFocus={(event) => event.currentTarget.select()}
        />
        <Button
          onClick={async () => {
            try {
              await navigator.clipboard.writeText(link)
              setCopyMessage('Link copied.')
            } catch {
              setCopyMessage('Select and copy the link above manually.')
            }
          }}
        >
          Copy link
        </Button>
        <p role="status">{copyMessage}</p>
        <Button
          variant="text"
          onClick={() => {
            setLink('')
            setCopyMessage('')
          }}
        >
          Invite another person
        </Button>
      </div>
    )
  return (
    <ActionForm
      label="Create invitation"
      submit={async (data) => {
        const result = await workspaceApi.invite(
          workspace.id,
          text(data, 'email'),
          text(data, 'role') as Role,
        )
        setLink(
          frontendInvitationLink(window.location.origin, {
            id: result.invitation.id,
            token: result.token,
          }),
        )
      }}
    >
      {(error) => (
        <>
          <FormField
            name="email"
            label="Email address"
            type="email"
            required
            maxLength={320}
            error={error('email')}
          />
          <SelectField label="Role" name="role" defaultValue="Developer">
            {assignableRoles(workspace.role).map((role) => (
              <option key={role}>{role}</option>
            ))}
          </SelectField>
        </>
      )}
    </ActionForm>
  )
}
export function WorkspaceInvitations({ workspace }: { workspace: Workspace }) {
  const [page, setPage] = useState(1)
  const invitations = useQuery({
    queryKey: ['phase2', workspace.id, 'invitations', page],
    queryFn: ({ signal }) =>
      workspaceApi.invitations(workspace.id, page, signal),
  })
  return (
    <section className="workspace-card">
      <div className="workspace-section-heading">
        <div>
          <h2>Invitations</h2>
          <p className="muted">Invite teammates and track their responses.</p>
        </div>
        <Modal
          title="Invite a teammate"
          description="Share the link with the invited email address. The recipient must verify their email before responding."
          trigger="Invite teammate"
        >
          {() => <InviteForm workspace={workspace} />}
        </Modal>
      </div>
      <QueryState
        pending={invitations.isPending}
        error={invitations.error}
        retry={invitations.refetch}
      >
        {!invitations.data?.items.length && (
          <Empty>No invitations on this page.</Empty>
        )}
        <ul className="workspace-rows">
          {invitations.data?.items.map((invitation) => (
            <li key={invitation.id}>
              <div>
                <strong>{invitation.email}</strong>
                <p className="muted">
                  {invitation.role} · Expires{' '}
                  {new Date(invitation.expiresAt).toLocaleString()}
                </p>
              </div>
              <div className="workspace-actions">
                <span className="workspace-badge" data-status={invitation.status}>{invitation.status}</span>
                {invitation.status === 'Pending' &&
                  assignableRoles(workspace.role).includes(invitation.role) && (
                    <ConfirmAction
                      title="Revoke invitation?"
                      description={`The invitation for ${invitation.email} will no longer work.`}
                      label="Revoke"
                      run={() =>
                        workspaceApi.revoke(workspace.id, invitation.id)
                      }
                    />
                  )}
              </div>
            </li>
          ))}
        </ul>
        {invitations.data && (
          <Pager page={page} total={invitations.data.total} change={setPage} />
        )}
      </QueryState>
    </section>
  )
}
