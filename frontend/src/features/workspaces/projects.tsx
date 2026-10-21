import { useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { Button } from '@/components/ui/button'
import { FormField } from '@/components/ui/form-field'
import {
  projectApi,
  workspaceApi,
  type Member,
  type Project,
  type ProjectFilters,
  type ProjectInput,
  type Workspace,
} from './api'
import { managesProject, managesWorkspace } from './rules'
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

function projectInput(data: FormData): ProjectInput {
  return {
    name: text(data, 'name'),
    projectKey: text(data, 'projectKey').toUpperCase(),
    description: text(data, 'description') || null,
    leadUserId: text(data, 'leadUserId') || null,
    startsOn: text(data, 'startsOn') || null,
    targetOn: text(data, 'targetOn') || null,
  }
}
function ProjectForm({
  workspaceId,
  project: currentProject,
  close,
  created,
}: {
  workspaceId: string
  project?: Project
  close: () => void
  created?: (project: Project) => void
}) {
  // Freeze both the displayed fields and their version while this editor is open.
  const [project] = useState(currentProject)
  const members = useQuery({
    queryKey: ['phase2', workspaceId, 'members'],
    queryFn: ({ signal }) => workspaceApi.members(workspaceId, signal),
  })
  const [conflict, setConflict] = useState(false)
  const [startDate, setStartDate] = useState(project?.startsOn || '')
  return (
    <QueryState
      pending={members.isPending}
      error={members.error}
      retry={members.refetch}
    >
      {conflict ? (
        <div>
          <p className="error-notice" role="alert">
            The project changed while you were editing. Close this dialog and
            reopen it to edit the latest version. Your changes were not saved.
          </p>
          <Button onClick={close}>Close and reload</Button>
        </div>
      ) : (
        <ActionForm
          label={project ? 'Save project' : 'Create project'}
          done={close}
          onConflict={(error) => {
            if (
              project &&
              /^(The project changed|The resource changed)/.test(error.message)
            )
              setConflict(true)
          }}
          submit={async (data) => {
            const input = projectInput(data)
            const result = project
              ? await projectApi.update(
                  workspaceId,
                  project.id,
                  input,
                  project.version,
                )
              : await projectApi.create(workspaceId, input)
            created?.(result)
          }}
        >
          {(error) => (
            <>
              <FormField
                label="Project name"
                name="name"
                defaultValue={project?.name}
                required
                maxLength={100}
                pattern=".*\S.*"
                error={error('name')}
              />
              <FormField
                label="Project key"
                name="projectKey"
                defaultValue={project?.projectKey}
                required
                minLength={2}
                maxLength={10}
                pattern="[A-Za-z][A-Za-z0-9]{1,9}"
                hint="2–10 letters or digits, starting with a letter. Keys must be unique in this workspace, including archived projects. A key cannot change once work items exist."
                error={error('projectKey')}
              />
              <label className="workspace-textarea">
                Description
                <textarea
                  name="description"
                  defaultValue={project?.description || ''}
                  maxLength={4000}
                />
              </label>
              <SelectField
                label="Project lead"
                name="leadUserId"
                defaultValue={project?.leadUserId || ''}
              >
                <option value="">No lead</option>
                {members.data?.map((member) => (
                  <option value={member.userId} key={member.userId}>
                    {member.displayName} · {member.email}
                  </option>
                ))}
              </SelectField>
              <div className="workspace-columns">
                <FormField
                  label="Start date"
                  name="startsOn"
                  type="date"
                  value={startDate}
                  onChange={(event) => setStartDate(event.target.value)}
                  error={error('startsOn')}
                />
                <FormField
                  label="Target date"
                  name="targetOn"
                  type="date"
                  defaultValue={project?.targetOn || ''}
                  min={startDate || undefined}
                  error={error('targetOn')}
                />
              </div>
            </>
          )}
        </ActionForm>
      )}
    </QueryState>
  )
}
export function Projects({
  workspace,
  navigate,
}: {
  workspace: Workspace
  navigate: (path: string) => void
}) {
  const [filters, setFilters] = useState<ProjectFilters>({
    search: '',
    archived: 'false',
    leadUserId: '',
    sort: 'name',
    descending: false,
    page: 1,
  })
  const [search, setSearch] = useState('')
  const projects = useQuery({
    queryKey: ['phase2', workspace.id, 'projects', filters],
    queryFn: ({ signal }) => projectApi.list(workspace.id, filters, signal),
  })
  const members = useQuery({
    queryKey: ['phase2', workspace.id, 'members'],
    queryFn: ({ signal }) => workspaceApi.members(workspace.id, signal),
  })
  const change = (patch: Partial<ProjectFilters>) =>
    setFilters((previous) => ({ ...previous, ...patch, page: patch.page || 1 }))
  const open = (project: Project) =>
    navigate(`/workspaces/${workspace.id}/projects/${project.id}`)
  return (
    <section className="workspace-card">
      <div className="workspace-section-heading">
        <div>
          <h2>Projects</h2>
          <p className="muted">
            A clear place for your team’s next piece of work.
          </p>
        </div>
        {managesWorkspace(workspace.role) && (
          <Modal title="Create a project" trigger="New project">
            {(close) => (
              <ProjectForm
                workspaceId={workspace.id}
                close={close}
                created={open}
              />
            )}
          </Modal>
        )}
      </div>
      <form
        className="workspace-filters"
        onSubmit={(event) => {
          event.preventDefault()
          change({ search })
        }}
      >
        <FormField
          label="Search projects"
          value={search}
          maxLength={100}
          placeholder="Name or key"
          onChange={(event) => setSearch(event.target.value)}
        />
        <Button variant="secondary" type="submit">
          Search
        </Button>
        <SelectField
          label="Status"
          value={filters.archived}
          onChange={(event) => change({ archived: event.target.value })}
        >
          <option value="false">Active</option>
          <option value="true">Archived</option>
          <option value="">All projects</option>
        </SelectField>
        <SelectField
          label="Lead"
          value={filters.leadUserId}
          onChange={(event) => change({ leadUserId: event.target.value })}
          disabled={!members.data}
        >
          <option value="">All leads</option>
          {members.data?.map((member) => (
            <option key={member.userId} value={member.userId}>
              {member.displayName}
            </option>
          ))}
        </SelectField>
        <SelectField
          label="Sort by"
          value={filters.sort}
          onChange={(event) => change({ sort: event.target.value })}
        >
          <option value="name">Name</option>
          <option value="key">Key</option>
          <option value="createdAt">Created</option>
          <option value="updatedAt">Updated</option>
        </SelectField>
        <SelectField
          label="Order"
          value={String(filters.descending)}
          onChange={(event) =>
            change({ descending: event.target.value === 'true' })
          }
        >
          <option value="false">Ascending</option>
          <option value="true">Descending</option>
        </SelectField>
      </form>
      {members.isError && (
        <p role="status" className="muted">
          Lead filters are unavailable.{' '}
          <button className="text-link" onClick={() => void members.refetch()}>
            Retry loading members
          </button>
        </p>
      )}
      <QueryState
        pending={projects.isPending}
        error={projects.error}
        retry={projects.refetch}
      >
        {!projects.data?.items.length && (
          <Empty>
            No projects match these filters.
            {managesWorkspace(workspace.role)
              ? ' Create a project or adjust your filters.'
              : ' Ask an Owner or Admin for project access.'}
          </Empty>
        )}
        <ul className="workspace-projects">
          {projects.data?.items.map((project) => (
            <li key={project.id}>
              <a
                href={`/workspaces/${workspace.id}/projects/${project.id}`}
                onClick={(event) => {
                  if (
                    !event.metaKey &&
                    !event.ctrlKey &&
                    !event.shiftKey &&
                    !event.altKey &&
                    event.button === 0
                  ) {
                    event.preventDefault()
                    open(project)
                  }
                }}
              >
                <div className="workspace-section-heading">
                  <span className="workspace-key">{project.projectKey}</span>
                  {project.archivedAt && (
                    <span className="workspace-badge" data-status="archived">Archived</span>
                  )}
                </div>
                <h3>{project.name}</h3>
                <p className="muted workspace-project-description">
                  {project.description || 'No description yet.'}
                </p>
                <span className="workspace-project-date">
                  {project.targetOn
                    ? `Target ${project.targetOn}`
                    : 'No target date'}{' '}
                  <span aria-hidden="true">↗</span>
                </span>
              </a>
            </li>
          ))}
        </ul>
        {projects.data && (
          <Pager
            page={filters.page}
            total={projects.data.total}
            change={(page) => change({ page })}
          />
        )}
      </QueryState>
    </section>
  )
}
export function ProjectDetail({
  workspace,
  projectId,
}: {
  workspace: Workspace
  projectId: string
}) {
  const project = useQuery({
    queryKey: ['phase2', workspace.id, 'project', projectId],
    queryFn: ({ signal }) => projectApi.get(workspace.id, projectId, signal),
  })
  return (
    <QueryState
      pending={project.isPending}
      error={project.error}
      retry={project.refetch}
    >
      {project.data && (
        <ProjectContent workspace={workspace} project={project.data} />
      )}
    </QueryState>
  )
}
function ProjectContent({
  workspace,
  project,
}: {
  workspace: Workspace
  project: Project
}) {
  const members = useQuery({
    queryKey: ['phase2', workspace.id, 'members'],
    queryFn: ({ signal }) => workspaceApi.members(workspace.id, signal),
  })
  const manage = managesProject(workspace.role)
  const lead = members.data?.find(
    (member) => member.userId === project.leadUserId,
  )
  return (
    <div className="workspace-stack">
      <section className="workspace-card">
        <div className="workspace-section-heading">
          <div>
            <span className="workspace-key">{project.projectKey}</span>
            <h2>{project.name}</h2>
          </div>
          <div className="workspace-actions">
            <span className="workspace-badge" data-status={project.archivedAt ? 'archived' : 'active'}>
              {project.archivedAt ? 'Archived · Read only' : 'Active'}
            </span>
            {manage && (
              <>
                {!project.archivedAt && (
                  <Modal title="Edit project" trigger="Edit project">
                    {(close) => (
                      <ProjectForm
                        workspaceId={workspace.id}
                        project={project}
                        close={close}
                      />
                    )}
                  </Modal>
                )}
                <ConfirmAction
                  title={
                    project.archivedAt ? 'Restore project?' : 'Archive project?'
                  }
                  description={
                    project.archivedAt
                      ? 'Restore this project to allow changes again.'
                      : 'The project will become read only. You can restore it later.'
                  }
                  label={project.archivedAt ? 'Restore' : 'Archive'}
                  run={() =>
                    projectApi.archive(
                      workspace.id,
                      project.id,
                      !project.archivedAt,
                    )
                  }
                />
              </>
            )}
          </div>
        </div>
        <p className="workspace-description">
          {project.description || 'No description yet.'}
        </p>
        <dl className="workspace-metadata">
          <div>
            <dt>Project lead</dt>
            <dd>
              {lead?.displayName ||
                (project.leadUserId ? 'Loading member…' : 'Not assigned')}
            </dd>
          </div>
          <div>
            <dt>Start date</dt>
            <dd>{project.startsOn || 'Not set'}</dd>
          </div>
          <div>
            <dt>Target date</dt>
            <dd>{project.targetOn || 'Not set'}</dd>
          </div>
        </dl>
        {project.archivedAt && (
          <p className="workspace-readonly">
            This project is archived. Restore it before editing settings or
            membership.
          </p>
        )}
      </section>
      <ProjectMembers
        workspace={workspace}
        project={project}
        available={members.data || []}
        availableError={members.error}
        retryAvailable={members.refetch}
      />
    </div>
  )
}
function ProjectMembers({
  workspace,
  project,
  available,
  availableError,
  retryAvailable,
}: {
  workspace: Workspace
  project: Project
  available: Member[]
  availableError: unknown
  retryAvailable: () => unknown
}) {
  const [page, setPage] = useState(1)
  const members = useQuery({
    queryKey: ['phase2', workspace.id, 'project', project.id, 'members', page],
    queryFn: ({ signal }) =>
      projectApi.members(workspace.id, project.id, page, signal),
  })
  const writable = managesProject(workspace.role) && !project.archivedAt
  return (
    <section className="workspace-card">
      <div className="workspace-section-heading">
        <div>
          <h2>Project members</h2>
          <p className="muted">
            Workspace membership alone does not grant project access to
            Managers, Developers or Viewers.
          </p>
        </div>
        {writable && (
          <Modal
            title="Add project member"
            description="Select an active workspace member. The server checks for existing project membership."
            trigger="Add member"
          >
            {(close) => (
              <QueryState
                pending={!available.length && !availableError}
                error={availableError}
                retry={retryAvailable}
              >
                <ActionForm
                  label="Add member"
                  submit={(data) =>
                    projectApi.addMember(
                      workspace.id,
                      project.id,
                      text(data, 'userId'),
                    )
                  }
                  done={close}
                >
                  {(error) => (
                    <>
                      <SelectField
                        label="Workspace member"
                        name="userId"
                        required
                        defaultValue=""
                      >
                        <option value="" disabled>
                          Select a member
                        </option>
                        {available.map((member) => (
                          <option key={member.userId} value={member.userId}>
                            {member.displayName} · {member.email}
                          </option>
                        ))}
                      </SelectField>
                      {error('userId') && (
                        <p className="field-error">{error('userId')}</p>
                      )}
                    </>
                  )}
                </ActionForm>
              </QueryState>
            )}
          </Modal>
        )}
      </div>
      <QueryState
        pending={members.isPending}
        error={members.error}
        retry={members.refetch}
      >
        {!members.data?.items.length && <Empty>No members on this page.</Empty>}
        <ul className="workspace-rows">
          {members.data?.items.map((member) => (
            <li key={member.userId}>
              <div className="workspace-person">
                <span className="workspace-avatar" aria-hidden="true">
                  {member.displayName.slice(0, 1)}
                </span>
                <div>
                  <strong>
                    {member.displayName}
                    {member.userId === project.leadUserId ? ' · Lead' : ''}
                  </strong>
                  <span className="muted">{member.email}</span>
                </div>
              </div>
              <div className="workspace-actions">
                <span className="workspace-badge">{member.role}</span>
                {writable &&
                  (member.userId === project.leadUserId ? (
                    <span className="muted">
                      Change the lead before removing
                    </span>
                  ) : (
                    <ConfirmAction
                      title={`Remove ${member.displayName}?`}
                      description="They will lose this project membership. Their workspace membership and historical records will be kept."
                      label="Remove"
                      run={() =>
                        projectApi.removeMember(
                          workspace.id,
                          project.id,
                          member.userId,
                        )
                      }
                    />
                  ))}
              </div>
            </li>
          ))}
        </ul>
        {members.data && (
          <Pager page={page} total={members.data.total} change={setPage} />
        )}
      </QueryState>
    </section>
  )
}
