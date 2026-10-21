import { api } from '@/lib/api'

export type Role = 'Owner' | 'Admin' | 'Manager' | 'Developer' | 'Viewer'
export type Workspace = { id: string; name: string; role: Role }
export type Member = {
  userId: string
  email: string
  displayName: string
  role: Role
}
export type Page<T> = {
  items: T[]
  total: number
  page: number
  pageSize: number
}
export type Invitation = {
  id: string
  workspaceId: string
  email: string
  role: Role
  status: 'Pending' | 'Accepted' | 'Expired' | 'Revoked'
  createdAt: string
  expiresAt: string
}
export type CreatedInvitation = {
  invitation: Invitation
  token: string
  invitationUrl: string
}
export type Project = {
  id: string
  workspaceId: string
  name: string
  projectKey: string
  description: string | null
  leadUserId: string | null
  startsOn: string | null
  targetOn: string | null
  archivedAt: string | null
  createdAt: string
  updatedAt: string
  version: number
}
export type ProjectInput = Pick<
  Project,
  'name' | 'projectKey' | 'description' | 'leadUserId' | 'startsOn' | 'targetOn'
>
export type ProjectFilters = {
  search: string
  archived: string
  leadUserId: string
  sort: string
  descending: boolean
  page: number
}
const base = (workspaceId: string) => `/workspaces/${workspaceId}`
const send = <T>(path: string, method: string, data?: unknown) =>
  api<T>(
    path,
    { method, ...(data === undefined ? {} : { body: JSON.stringify(data) }) },
    true,
  )
const read = <T>(path: string, signal?: AbortSignal) =>
  api<T>(path, { signal }, true)

export const workspaceApi = {
  list: (signal?: AbortSignal) => read<Workspace[]>('/workspaces', signal),
  get: (id: string, signal?: AbortSignal) => read<Workspace>(base(id), signal),
  create: (name: string) => send<Workspace>('/workspaces', 'POST', { name }),
  rename: (id: string, name: string) =>
    send<Workspace>(base(id), 'PUT', { name }),
  members: (id: string, signal?: AbortSignal) =>
    read<Member[]>(`${base(id)}/members`, signal),
  addMember: (id: string, email: string, role: Role) =>
    send<Member>(`${base(id)}/members`, 'POST', { email, role }),
  changeRole: (id: string, userId: string, role: Role) =>
    send<Member>(`${base(id)}/members/${userId}/role`, 'PUT', { role }),
  removeMember: (id: string, userId: string) =>
    send<void>(`${base(id)}/members/${userId}`, 'DELETE'),
  transfer: (id: string, newOwnerUserId: string) =>
    send<void>(`${base(id)}/ownership/transfer`, 'POST', { newOwnerUserId }),
  invitations: (id: string, page: number, signal?: AbortSignal) =>
    read<Page<Invitation>>(
      `${base(id)}/invitations?page=${page}&pageSize=20`,
      signal,
    ),
  invite: (id: string, email: string, role: Role) =>
    send<CreatedInvitation>(`${base(id)}/invitations`, 'POST', { email, role }),
  revoke: (id: string, invitationId: string) =>
    send<void>(`${base(id)}/invitations/${invitationId}/revoke`, 'POST'),
}
export const invitationApi = {
  get: (id: string, signal?: AbortSignal) =>
    read<Invitation>(`/invitations/${id}`, signal),
  respond: (token: string, accept: boolean) =>
    send<void>(`/invitations/${accept ? 'accept' : 'reject'}`, 'POST', {
      token,
    }),
}
export const projectApi = {
  list: (id: string, filters: ProjectFilters, signal?: AbortSignal) => {
    const query = new URLSearchParams({
      sort: filters.sort,
      descending: String(filters.descending),
      page: String(filters.page),
      pageSize: '20',
    })
    if (filters.search.trim()) query.set('search', filters.search.trim())
    if (filters.archived) query.set('archived', filters.archived)
    if (filters.leadUserId) query.set('leadUserId', filters.leadUserId)
    return read<Page<Project>>(`${base(id)}/projects?${query}`, signal)
  },
  get: (id: string, projectId: string, signal?: AbortSignal) =>
    read<Project>(`${base(id)}/projects/${projectId}`, signal),
  create: (id: string, data: ProjectInput) =>
    send<Project>(`${base(id)}/projects`, 'POST', data),
  update: (
    id: string,
    projectId: string,
    data: ProjectInput,
    version: number,
  ) =>
    send<Project>(`${base(id)}/projects/${projectId}`, 'PUT', {
      ...data,
      version,
    }),
  archive: (id: string, projectId: string, archived: boolean) =>
    send<Project>(
      `${base(id)}/projects/${projectId}/${archived ? 'archive' : 'restore'}`,
      'POST',
    ),
  members: (
    id: string,
    projectId: string,
    page: number,
    signal?: AbortSignal,
  ) =>
    read<Page<Member>>(
      `${base(id)}/projects/${projectId}/members?page=${page}&pageSize=20`,
      signal,
    ),
  addMember: (id: string, projectId: string, userId: string) =>
    send<Member>(`${base(id)}/projects/${projectId}/members`, 'POST', {
      userId,
    }),
  removeMember: (id: string, projectId: string, userId: string) =>
    send<void>(`${base(id)}/projects/${projectId}/members/${userId}`, 'DELETE'),
}
