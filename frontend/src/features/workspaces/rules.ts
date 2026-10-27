import type { Role } from './api'
import { ApiError } from '@/lib/api'

export function memberErrorMessage(
  error: unknown,
  scope: 'workspace' | 'project',
) {
  if (!(error instanceof ApiError)) return undefined
  if (error.status === 403)
    return 'You don’t have permission to add this member or assign this role. Ask a workspace Owner or Admin for help.'
  if (error.status === 404)
    return scope === 'workspace'
      ? 'We couldn’t find an active account with this email, or you no longer have access to this workspace. Check the email, or send an invitation if they haven’t joined FlowForge yet.'
      : 'This project or member is no longer available to you. Refresh the page and choose an active workspace member.'
  if (error.status === 409) {
    if (scope === 'workspace')
      return 'This person is already a member of this workspace. You can find them in the member list.'
    if (/already.*member/i.test(error.message))
      return 'This person is already a member of this project. You can find them in the member list.'
    if (/archiv/i.test(error.message))
      return 'This project is archived. Restore it before adding members.'
    return 'The project changed while you were adding a member. Refresh the member list and try again.'
  }
  if (error.status === 400)
    return scope === 'workspace'
      ? 'Check the email address and selected role, then try again.'
      : 'Choose an active workspace member. Add them to the workspace first if needed.'
  return undefined
}

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
  projectSection?:
    | 'work-items'
    | 'labels'
    | 'activity'
    | 'workflows'
    | 'dashboard'
    | 'ai'
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
    /^\/workspaces\/([^/]+)(?:\/(projects|members|invitations|settings))?(?:\/([^/]+))?(?:\/(work-items|labels|activity|workflows|dashboard|ai)(?:\/([^/]+))?)?\/?$/.exec(
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
