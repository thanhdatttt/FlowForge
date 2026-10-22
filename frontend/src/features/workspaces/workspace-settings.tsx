import { useQuery } from '@tanstack/react-query'
import { FormField } from '@/components/ui/form-field'
import { workspaceApi, type Workspace } from './api'
import { managesWorkspace } from './rules'
import { ActionForm, Empty, QueryState, SelectField } from './shared'
import { text } from './rules'

export function WorkspaceSettings({
  workspace,
  userId,
}: {
  workspace: Workspace
  userId: string
}) {
  const members = useQuery({
    queryKey: ['phase2', workspace.id, 'members'],
    queryFn: ({ signal }) => workspaceApi.members(workspace.id, signal),
  })
  return (
    <div className="workspace-stack">
      <section className="workspace-card">
        <h2>Workspace details</h2>
        <p className="muted">Your shared space for people and projects.</p>
        {managesWorkspace(workspace.role) ? (
          <ActionForm
            key={workspace.name}
            submit={(data) =>
              workspaceApi.rename(workspace.id, text(data, 'name'))
            }
          >
            {(error) => (
              <FormField
                label="Workspace name"
                name="name"
                defaultValue={workspace.name}
                required
                maxLength={100}
                pattern=".*\S.*"
                error={error('name')}
              />
            )}
          </ActionForm>
        ) : (
          <p>
            {workspace.name} · {workspace.role}
          </p>
        )}
      </section>
      {workspace.role === 'Owner' && (
        <section className="workspace-card">
          <h2>Transfer ownership</h2>
          <p className="muted">
            You will become an Admin. The new Owner must be another active
            member with a verified email. The server checks eligibility when you
            confirm.
          </p>
          <QueryState
            pending={members.isPending}
            error={members.error}
            retry={members.refetch}
          >
            {(members.data?.filter((member) => member.userId !== userId)
              .length ?? 0) > 0 ? (
              <ActionForm
                label="Transfer ownership"
                submit={(data) =>
                  workspaceApi.transfer(
                    workspace.id,
                    text(data, 'newOwnerUserId'),
                  )
                }
              >
                {() => (
                  <>
                    <SelectField
                      label="New Owner"
                      name="newOwnerUserId"
                      required
                      defaultValue=""
                    >
                      <option value="" disabled>
                        Select a member
                      </option>
                      {members.data
                        ?.filter((member) => member.userId !== userId)
                        .map((member) => (
                          <option key={member.userId} value={member.userId}>
                            {member.displayName} · {member.email}
                          </option>
                        ))}
                    </SelectField>
                    <label className="workspace-check">
                      <input type="checkbox" required /> I understand that I
                      will give up ownership and become an Admin.
                    </label>
                  </>
                )}
              </ActionForm>
            ) : (
              <Empty>Add another member before transferring ownership.</Empty>
            )}
          </QueryState>
        </section>
      )}
    </div>
  )
}
