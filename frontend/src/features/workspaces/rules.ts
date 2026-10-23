import type { Role } from './api'

export const managesWorkspace = (role: Role) =>
  role === 'Owner' || role === 'Admin'
export const assignableRoles = (role: Role): Role[] =>
  role === 'Owner'
    ? ['Admin', 'Manager', 'Developer', 'Viewer']
    : role === 'Admin'
      ? ['Manager', 'Developer', 'Viewer']
      : []
export const canManageMember = (actor: Role, target: Role) =>
  assignableRoles(actor).includes(target)
export const managesProject = (role: Role) =>
  managesWorkspace(role) || role === 'Manager'

export type InvitationLink = { id: string; token: string }
const uuid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i
export function parseInvitationLink(value: string): InvitationLink | null {
  try {
    const url = new URL(value, 'http://localhost')
    if (!['http:', 'https:'].includes(url.protocol)) return null
    const match = /^\/(?:api\/v1\/)?invitations\/([^/]+)\/?$/.exec(url.pathname)
    const token = new URLSearchParams(url.hash.slice(1)).get('token')
    if (
      !match ||
      !uuid.test(match[1]) ||
      !token ||
      token.length > 256 ||
      /\s/.test(token)
    )
      return null
    return { id: match[1], token }
  } catch {
    return null
  }
}
export function frontendInvitationLink(
  origin: string,
  invitation: InvitationLink,
) {
  return `${origin}/invitations/${invitation.id}#${new URLSearchParams({ token: invitation.token })}`
}
export type Route = {
  workspaceId?: string
  section: 'projects' | 'members' | 'invitations' | 'settings' | 'workflows'
  projectId?: string
  projectSection?: 'work-items' | 'labels' | 'activity' | 'workflows'
  itemId?: string
  workflowId?: string
  executionId?: string
}
export function parseRoute(path: string): Route {
  const workflow =
    /^\/workspaces\/([^/]+)\/workflows(?:\/([^/]+)(?:\/executions\/([^/]+))?)?\/?$/.exec(
      path,
    )
  if (
    workflow &&
    workflow
      .slice(1)
      .filter(Boolean)
      .every((id) => uuid.test(id))
  )
    return {
      workspaceId: workflow[1],
      section: 'workflows',
      workflowId: workflow[2],
      executionId: workflow[3],
    }
  const execution =
    /^\/workspaces\/([^/]+)\/workflow-executions\/([^/]+)\/?$/.exec(path)
  if (execution && uuid.test(execution[1]) && uuid.test(execution[2]))
    return {
      workspaceId: execution[1],
      section: 'workflows',
      executionId: execution[2],
    }

  const match =
    /^\/workspaces\/([^/]+)(?:\/(projects|members|invitations|settings))?(?:\/([^/]+))?(?:\/(work-items|labels|activity|workflows)(?:\/([^/]+))?)?\/?$/.exec(
      path,
    )
  if (
    !match ||
    !uuid.test(match[1]) ||
    (match[3] && (match[2] !== 'projects' || !uuid.test(match[3]))) ||
    (match?.[4] && !match[3]) ||
    (match?.[5] && (match[4] !== 'work-items' || !uuid.test(match[5])))
  )
    return { section: 'projects' }
  return {
    workspaceId: match[1],
    section: (match[2] as Route['section']) || 'projects',
    projectId: match[3],
    ...(match[4]
      ? { projectSection: match[4] as Route['projectSection'] }
      : {}),
    ...(match[5] ? { itemId: match[5] } : {}),
  }
}

export const text = (data: FormData, name: string) =>
  String(data.get(name) || '').trim()
