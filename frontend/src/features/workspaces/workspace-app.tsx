import { useEffect, useState, type ReactNode } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { FolderKanban, LogOut, Mail, Settings, Users } from 'lucide-react'
import { Brand } from '@/components/brand'
import { Button } from '@/components/ui/button'
import { FormField } from '@/components/ui/form-field'
import { authApi, type CurrentUser } from '@/features/auth/api'
import { useSessionStore } from '@/features/auth/session-store'
import { AccountPanel } from '@/features/auth/components/account-panel'
import { workspaceApi, type Workspace } from './api'
import { managesWorkspace, parseRoute, type InvitationLink } from './rules'
import {
  ActionForm,
  Empty,
  ErrorNotice,
  Modal,
  QueryState,
  SelectField,
} from './shared'
import { text } from './rules'
import { WorkspaceSettings } from './workspace-settings'
import { WorkspaceMembers } from './workspace-members'
import { WorkspaceInvitations } from './workspace-invitations'
import { Projects } from './projects'
import { ProjectWork } from '@/features/work-items/project'
import { InvitationEntry, InvitationPage } from './invitation-page'

export function WorkspaceApp({
  path,
  navigate: changeRoute,
  invitation,
  openInvitation,
  clearInvitation,
}: {
  path: string
  navigate: (path: string) => void
  invitation: InvitationLink | null
  openInvitation: (invitation: InvitationLink) => void
  clearInvitation: () => void
}) {
  const navigate = (next: string) => {
    setAccount(false)
    changeRoute(next)
  }
  const client = useQueryClient()
  const [accountPath, setAccountPath] = useState<string | null>(null)
  const account = accountPath === path
  const setAccount = (value: boolean) => setAccountPath(value ? path : null)
  const [logoutError, setLogoutError] = useState<unknown>(null)
  const [signingOut, setSigningOut] = useState(false)
  const user = useQuery({
    queryKey: ['auth', 'me'],
    queryFn: ({ signal }) => authApi.me(signal),
    retry: false,
  })
  const workspaces = useQuery({
    queryKey: ['phase2', 'workspaces'],
    queryFn: ({ signal }) => workspaceApi.list(signal),
    retry: false,
  })
  const route = parseRoute(path)
  const selected = useQuery({
    queryKey: ['phase2', route.workspaceId, 'workspace'],
    queryFn: ({ signal }) => workspaceApi.get(route.workspaceId!, signal),
    enabled: !!route.workspaceId,
    retry: false,
  })
  const workspace = selected.data
  useEffect(() => {
    document.title = `${invitation ? 'Invitation' : account ? 'Your account' : workspace?.name || 'Workspaces'} · FlowForge`
  }, [workspace?.name, invitation, account])
  async function logout() {
    setSigningOut(true)
    setLogoutError(null)
    try {
      await client.cancelQueries()
      const tokens = useSessionStore.getState().tokens
      if (tokens) await authApi.signOut(tokens.refreshToken)
      useSessionStore.getState().clear()
      client.clear()
      clearInvitation()
      navigate('/login')
    } catch (error) {
      setLogoutError(error)
    } finally {
      setSigningOut(false)
    }
  }
  function finish(workspaceId?: string) {
    clearInvitation()
    setAccount(false)
    navigate(
      workspaceId ? `/workspaces/${workspaceId}/projects` : '/workspaces',
    )
  }
  return (
    <div className="workspace-layout">
      <a href="#workspace-content" className="skip-link">
        Skip to workspace content
      </a>
      <aside className="workspace-sidebar">
        <Brand />
        <SelectField
          label="Current workspace"
          value={route.workspaceId || ''}
          onChange={(event) =>
            navigate(
              event.target.value
                ? `/workspaces/${event.target.value}/projects`
                : '/workspaces',
            )
          }
        >
          <option value="">All workspaces</option>
          {workspaces.data?.map((item) => (
            <option key={item.id} value={item.id}>
              {item.name}
            </option>
          ))}
          {workspace &&
            !workspaces.data?.some((item) => item.id === workspace.id) && (
              <option value={workspace.id}>{workspace.name}</option>
            )}
        </SelectField>
        <nav aria-label="Workspace navigation">
          <Nav
            path="/workspaces"
            active={!route.workspaceId && !invitation && !account}
            navigate={navigate}
          >
            <FolderKanban size={18} aria-hidden="true" /> Workspaces
          </Nav>
          {route.workspaceId && (
            <>
              <Nav
                path={`/workspaces/${route.workspaceId}/projects`}
                active={route.section === 'projects' && !invitation && !account}
                navigate={navigate}
              >
                <FolderKanban size={18} aria-hidden="true" /> Projects
              </Nav>
              <Nav
                path={`/workspaces/${route.workspaceId}/members`}
                active={route.section === 'members' && !invitation && !account}
                navigate={navigate}
              >
                <Users size={18} aria-hidden="true" /> Members
              </Nav>
              {workspace && managesWorkspace(workspace.role) && (
                <Nav
                  path={`/workspaces/${workspace.id}/invitations`}
                  active={
                    route.section === 'invitations' && !invitation && !account
                  }
                  navigate={navigate}
                >
                  <Mail size={18} aria-hidden="true" /> Invitations
                </Nav>
              )}
              <Nav
                path={`/workspaces/${route.workspaceId}/settings`}
                active={route.section === 'settings' && !invitation && !account}
                navigate={navigate}
              >
                <Settings size={18} aria-hidden="true" /> Settings
              </Nav>
            </>
          )}
        </nav>
        <div className="workspace-sidebar-footer">
          <button
            className="workspace-account-button"
            onClick={() => setAccount(true)}
          >
            <span className="workspace-avatar" aria-hidden="true">
              {user.data?.displayName.slice(0, 1) || '?'}
            </span>
            <span>
              {user.data?.displayName || 'Your account'}
              <small>
                {user.data?.emailVerified
                  ? 'Account settings'
                  : 'Verify your email'}
              </small>
            </span>
          </button>
          <Button
            variant="text"
            className="button--logout"
            onClick={() => void logout()}
            loading={signingOut}
          >
            <LogOut size={17} aria-hidden="true" /> Sign out
          </Button>
        </div>
      </aside>
      <main className="workspace-main" id="workspace-content" tabIndex={-1}>
        <header className="workspace-topbar">
          <div>
            <span className="eyebrow">
              FLOWFORGE / {workspace ? 'WORKSPACE' : 'YOUR SPACE'}
            </span>
            <h1>
              {account
                ? 'Your account'
                : invitation
                  ? 'Join your team'
                  : workspace?.name || 'Workspaces'}
            </h1>
          </div>
          <div className="workspace-actions">
            {workspace && (
              <span className="workspace-badge">{workspace.role}</span>
            )}
            <Button
              variant="secondary"
              onClick={() => {
                void client.invalidateQueries({ queryKey: ['phase2'] })
                void client.invalidateQueries({ queryKey: ['phase3'] })
                void user.refetch()
              }}
            >
              Refresh
            </Button>
          </div>
        </header>
        <ErrorNotice error={logoutError} />
        <QueryState
          pending={user.isPending}
          hasData={!!user.data}
          error={user.error}
          retry={user.refetch}
        >
          {user.data &&
            (account ? (
              <section className="workspace-card workspace-account">
                <AccountPanel />
                <Button variant="secondary" onClick={() => setAccount(false)}>
                  {invitation ? 'Back to invitation' : 'Back to workspace'}
                </Button>
              </section>
            ) : invitation ? (
              <InvitationPage
                key={invitation.id}
                invitation={invitation}
                user={user.data}
                verify={() => setAccount(true)}
                finish={finish}
              />
            ) : route.workspaceId ? (
              <QueryState
                pending={selected.isPending}
                hasData={!!selected.data}
                error={selected.error}
                retry={selected.refetch}
              >
                {workspace && (
                  <WorkspaceContent
                    key={workspace.id}
                    workspace={workspace}
                    user={user.data}
                    route={route}
                    navigate={navigate}
                  />
                )}
              </QueryState>
            ) : (
              <WorkspaceHome
                pending={workspaces.isPending}
                error={workspaces.error}
                retry={workspaces.refetch}
                workspaces={workspaces.data || []}
                navigate={navigate}
                openInvitation={openInvitation}
              />
            ))}
        </QueryState>
      </main>
    </div>
  )
}
function Nav({
  path,
  active,
  navigate,
  children,
}: {
  path: string
  active: boolean
  navigate: (path: string) => void
  children: ReactNode
}) {
  return (
    <a
      href={path}
      aria-current={active ? 'page' : undefined}
      onClick={(event) => {
        if (
          event.button === 0 &&
          !event.metaKey &&
          !event.ctrlKey &&
          !event.shiftKey &&
          !event.altKey
        ) {
          event.preventDefault()
          navigate(path)
        }
      }}
    >
      {children}
    </a>
  )
}
function WorkspaceHome({
  pending,
  error,
  retry,
  workspaces,
  navigate,
  openInvitation,
}: {
  pending: boolean
  error: unknown
  retry: () => unknown
  workspaces: Workspace[]
  navigate: (path: string) => void
  openInvitation: (invitation: InvitationLink) => void
}) {
  return (
    <div className="workspace-stack">
      <section className="workspace-card">
        <div className="workspace-section-heading">
          <div>
            <h2>Your workspaces</h2>
            <p className="muted">
              Choose a team or create a space of your own.
            </p>
          </div>
          <Modal title="Create a workspace" trigger="New workspace">
            {(close) => (
              <ActionForm
                label="Create workspace"
                done={close}
                submit={async (data) => {
                  const result = await workspaceApi.create(text(data, 'name'))
                  navigate(`/workspaces/${result.id}/projects`)
                }}
              >
                {(error) => (
                  <FormField
                    label="Workspace name"
                    name="name"
                    required
                    maxLength={100}
                    pattern=".*\S.*"
                    error={error('name')}
                  />
                )}
              </ActionForm>
            )}
          </Modal>
        </div>
        <QueryState pending={pending} error={error} retry={retry}>
          {!workspaces.length && (
            <Empty>
              You don’t belong to a workspace yet. Create one or use an
              invitation below.
            </Empty>
          )}
          <ul className="workspace-rows">
            {workspaces.map((item) => (
              <li key={item.id}>
                <a
                  className="text-link"
                  href={`/workspaces/${item.id}/projects`}
                  onClick={(event) => {
                    if (
                      !event.ctrlKey &&
                      !event.metaKey &&
                      !event.shiftKey &&
                      !event.altKey &&
                      event.button === 0
                    ) {
                      event.preventDefault()
                      navigate(`/workspaces/${item.id}/projects`)
                    }
                  }}
                >
                  {item.name} <span aria-hidden="true">→</span>
                </a>
                <span className="workspace-badge">{item.role}</span>
              </li>
            ))}
          </ul>
        </QueryState>
      </section>
      <InvitationEntry open={openInvitation} />
    </div>
  )
}
function WorkspaceContent({
  workspace,
  user,
  route,
  navigate,
}: {
  workspace: Workspace
  user: CurrentUser
  route: ReturnType<typeof parseRoute>
  navigate: (path: string) => void
}) {
  if (route.section === 'members')
    return <WorkspaceMembers workspace={workspace} />
  if (route.section === 'settings')
    return <WorkspaceSettings workspace={workspace} userId={user.id} />
  if (route.section === 'invitations')
    return managesWorkspace(workspace.role) ? (
      <WorkspaceInvitations workspace={workspace} />
    ) : (
      <Empty>Only Owners and Admins can manage invitations.</Empty>
    )
  if (route.projectId)
    return (
      <ProjectWork
        key={route.projectId}
        workspace={workspace}
        route={route}
        userId={user.id}
        navigate={navigate}
      />
    )
  return <Projects workspace={workspace} navigate={navigate} />
}
