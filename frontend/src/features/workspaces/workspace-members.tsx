import { useQuery } from '@tanstack/react-query'
import { FormField } from '@/components/ui/form-field'
import { workspaceApi, type Role, type Workspace } from './api'
import { assignableRoles, canManageMember, managesWorkspace } from './rules'
import {
  ActionForm,
  ConfirmAction,
  Modal,
  QueryState,
  SelectField,
} from './shared'
import { text } from './rules'

export function WorkspaceMembers({ workspace }: { workspace: Workspace }) {
  const members = useQuery({
    queryKey: ['phase2', workspace.id, 'members'],
    queryFn: ({ signal }) => workspaceApi.members(workspace.id, signal),
  })
  const roles = assignableRoles(workspace.role)
  return (
    <section className="workspace-card">
      <div className="workspace-section-heading">
        <div>
          <h2>Workspace members</h2>
          <p className="muted">Manage who belongs to your team.</p>
        </div>
        {managesWorkspace(workspace.role) && (
          <Modal
            title="Add an existing member"
            description="Use the email of an active FlowForge account. For someone without an account, create an invitation instead."
            trigger="Add member"
          >
            {(close) => (
              <ActionForm
                label="Add member"
                submit={(data) =>
                  workspaceApi.addMember(
                    workspace.id,
                    text(data, 'email'),
                    text(data, 'role') as Role,
                  )
                }
                done={close}
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
                    <SelectField
                      label="Role"
                      name="role"
                      defaultValue="Developer"
                    >
                      {roles.map((role) => (
                        <option key={role}>{role}</option>
                      ))}
                    </SelectField>
                  </>
                )}
              </ActionForm>
            )}
          </Modal>
        )}
      </div>
      <QueryState
        pending={members.isPending}
        error={members.error}
        retry={members.refetch}
      >
        <ul className="workspace-rows">
          {members.data?.map((member) => (
            <li key={member.userId}>
              <div className="workspace-person">
                <span className="workspace-avatar" aria-hidden="true">
                  {member.displayName.slice(0, 1)}
                </span>
                <div>
                  <strong>{member.displayName}</strong>
                  <span className="muted">{member.email}</span>
                </div>
              </div>
              <div className="workspace-actions">
                <span className="workspace-badge">{member.role}</span>
                {canManageMember(workspace.role, member.role) && (
                  <>
                    <Modal
                      title={`Change role for ${member.displayName}`}
                      trigger="Change role"
                    >
                      {(close) => (
                        <ActionForm
                          submit={(data) =>
                            workspaceApi.changeRole(
                              workspace.id,
                              member.userId,
                              text(data, 'role') as Role,
                            )
                          }
                          done={close}
                        >
                          {() => (
                            <SelectField
                              label="Role"
                              name="role"
                              defaultValue={member.role}
                            >
                              {roles.map((role) => (
                                <option key={role}>{role}</option>
                              ))}
                            </SelectField>
                          )}
                        </ActionForm>
                      )}
                    </Modal>
                    <ConfirmAction
                      title={`Remove ${member.displayName}?`}
                      description="Their project access will end. Historical comments and activity will be kept. Rejoining will not restore their previous project access."
                      label="Remove"
                      run={() =>
                        workspaceApi.removeMember(workspace.id, member.userId)
                      }
                    />
                  </>
                )}
              </div>
            </li>
          ))}
        </ul>
      </QueryState>
    </section>
  )
}
