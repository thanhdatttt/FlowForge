import { api } from '@/lib/api'
import {
  projectApi,
  workspaceApi,
  type Member,
  type Page,
} from '@/features/workspaces/api'

export type ItemType = 'Task' | 'Issue'
export type Priority = 'Low' | 'Medium' | 'High' | 'Critical'
export type WorkItem = {
  id: string
  projectId: string
  key: string
  itemNumber: number
  title: string
  description: string | null
  type: ItemType
  status: string
  priority: Priority
  reporterUserId: string
  assigneeUserId: string | null
  parentId: string | null
  startDate: string | null
  dueDate: string | null
  estimateMinutes: number | null
  labelIds: string[]
  dependsOnItemIds: string[]
  isBlocked: boolean
  needsReassignment: boolean
  archivedAt: string | null
  createdAt: string
  updatedAt: string
  version: number
}
export type WorkItemInput = Pick<
  WorkItem,
  | 'title'
  | 'description'
  | 'type'
  | 'status'
  | 'priority'
  | 'assigneeUserId'
  | 'parentId'
  | 'startDate'
  | 'dueDate'
  | 'estimateMinutes'
  | 'labelIds'
>
export type Status = {
  id: string
  name: string
  category: string
  sortOrder: number
}
export type Label = { id: string; name: string; color: string | null }
export type Comment = {
  id: string
  authorUserId: string
  body: string
  mentionedUserIds: string[]
  createdAt: string
  updatedAt: string
  version: number
}
export type TimeEntry = {
  id: string
  userId: string
  minutes: number
  startedAt: string | null
  note: string | null
  createdAt: string
  updatedAt: string
}
export type TimeInput = Pick<TimeEntry, 'minutes' | 'startedAt' | 'note'>
export type Attachment = {
  id: string
  originalName: string
  contentType: string
  sizeBytes: number
  uploadedByUserId: string
  createdAt: string
}
export type Activity = {
  id: string
  projectId: string | null
  workItemId: string | null
  actorUserId: string | null
  actorKind: string
  action: string
  entityType: string
  entityId: string
  beforeValues: Record<string, unknown> | null
  afterValues: Record<string, unknown> | null
  correlationId: string | null
  occurredAt: string
  workflowExecutionId: string | null
}
export type ItemFilters = {
  search?: string
  status?: string
  priority?: string
  type?: string
  assigneeUserId?: string
  reporterUserId?: string
  labelId?: string
  dueBefore?: string
  dueAfter?: string
  archived?: 'false' | 'true'
  sort?: string
  descending?: boolean
  page?: number
  pageSize?: number
}
export type Scope = { workspaceId: string; projectId: string }
const base = (s: Scope) =>
  `/workspaces/${s.workspaceId}/projects/${s.projectId}`
const itemPath = (s: Scope, id: string) => `${base(s)}/work-items/${id}`
const read = <T>(path: string, signal?: AbortSignal) =>
  api<T>(path, { signal }, true)
const send = <T>(path: string, method: string, data?: unknown) =>
  api<T>(
    path,
    {
      method,
      ...(data === undefined ? {} : { body: JSON.stringify(data) }),
    },
    true,
  )
const paged = (path: string, page: number) => `${path}?page=${page}&pageSize=20`

// Enumerate every page for relation/member pickers: never treat one page as complete.
export async function collectPages<T>(
  load: (page: number) => Promise<Page<T>>,
): Promise<T[]> {
  const items: T[] = []
  for (let page = 1; ; page++) {
    const result = await load(page)
    items.push(...result.items)
    if (page * result.pageSize >= result.total) return items
    if (!result.items.length || result.pageSize < 1)
      throw new Error('The list changed. Refresh and try again.')
  }
}
export const workApi = {
  statuses: (s: Scope, signal?: AbortSignal) =>
    read<Status[]>(`${base(s)}/statuses`, signal),
  labels: (s: Scope, signal?: AbortSignal) =>
    read<Label[]>(`${base(s)}/labels`, signal),
  saveLabel: (s: Scope, data: Pick<Label, 'name' | 'color'>, id?: string) =>
    send<Label>(
      `${base(s)}/labels${id ? `/${id}` : ''}`,
      id ? 'PUT' : 'POST',
      data,
    ),
  deleteLabel: (s: Scope, id: string) =>
    send<void>(`${base(s)}/labels/${id}`, 'DELETE'),
  list: (s: Scope, filters: ItemFilters = {}, signal?: AbortSignal) => {
    const query = new URLSearchParams({
      page: String(filters.page ?? 1),
      pageSize: String(filters.pageSize ?? 20),
    })
    for (const [key, value] of Object.entries(filters))
      if (value !== undefined && value !== '') query.set(key, String(value))
    return read<Page<WorkItem>>(`${base(s)}/work-items?${query}`, signal)
  },
  get: (s: Scope, id: string, signal?: AbortSignal) =>
    read<WorkItem>(itemPath(s, id), signal),
  create: (s: Scope, input: WorkItemInput) =>
    send<WorkItem>(`${base(s)}/work-items`, 'POST', input),
  update: (s: Scope, id: string, input: WorkItemInput, version: number) =>
    send<WorkItem>(itemPath(s, id), 'PUT', { ...input, version }),
  archive: (s: Scope, id: string, archive: boolean) =>
    send<WorkItem>(
      `${itemPath(s, id)}/${archive ? 'archive' : 'restore'}`,
      'POST',
    ),
  addDependency: (s: Scope, id: string, dependsOnItemId: string) =>
    send<{ dependsOnItemId: string }>(
      `${itemPath(s, id)}/dependencies`,
      'POST',
      { dependsOnItemId },
    ),
  removeDependency: (s: Scope, id: string, dependencyId: string) =>
    send<void>(`${itemPath(s, id)}/dependencies/${dependencyId}`, 'DELETE'),
  comments: (s: Scope, id: string, page: number, signal?: AbortSignal) =>
    read<Page<Comment>>(paged(`${itemPath(s, id)}/comments`, page), signal),
  saveComment: (
    s: Scope,
    id: string,
    body: string,
    comment?: Pick<Comment, 'id' | 'version'>,
  ) =>
    send<Comment>(
      `${itemPath(s, id)}/comments${comment ? `/${comment.id}` : ''}`,
      comment ? 'PUT' : 'POST',
      { body, ...(comment ? { version: comment.version } : {}) },
    ),
  deleteComment: (s: Scope, id: string, commentId: string) =>
    send<void>(`${itemPath(s, id)}/comments/${commentId}`, 'DELETE'),
  timeEntries: (s: Scope, id: string, page: number, signal?: AbortSignal) =>
    read<Page<TimeEntry>>(
      paged(`${itemPath(s, id)}/time-entries`, page),
      signal,
    ),
  saveTime: (s: Scope, id: string, input: TimeInput, entryId?: string) =>
    send<TimeEntry>(
      `${itemPath(s, id)}/time-entries${entryId ? `/${entryId}` : ''}`,
      entryId ? 'PUT' : 'POST',
      input,
    ),
  deleteTime: (s: Scope, id: string, entryId: string) =>
    send<void>(`${itemPath(s, id)}/time-entries/${entryId}`, 'DELETE'),
  attachments: (s: Scope, id: string, page: number, signal?: AbortSignal) =>
    read<Page<Attachment>>(
      paged(`${itemPath(s, id)}/attachments`, page),
      signal,
    ),
  upload: (s: Scope, id: string, file: File) => {
    const body = new FormData()
    body.append('file', file)
    return api<Attachment>(
      `${itemPath(s, id)}/attachments`,
      { method: 'POST', body },
      true,
    )
  },
  download: (s: Scope, id: string, attachmentId: string) =>
    api<Blob>(
      `${itemPath(s, id)}/attachments/${attachmentId}`,
      {},
      true,
      'blob',
    ),
  deleteAttachment: (s: Scope, id: string, attachmentId: string) =>
    send<void>(`${itemPath(s, id)}/attachments/${attachmentId}`, 'DELETE'),
  activity: (s: Scope, page: number, itemId?: string, signal?: AbortSignal) => {
    const query = new URLSearchParams({
      projectId: s.projectId,
      page: String(page),
      pageSize: '20',
    })
    if (itemId) query.set('workItemId', itemId)
    return read<Page<Activity>>(
      `/workspaces/${s.workspaceId}/activity?${query}`,
      signal,
    )
  },
  members: async (s: Scope, signal?: AbortSignal): Promise<Member[]> => {
    const [workspace, project] = await Promise.all([
      workspaceApi.members(s.workspaceId, signal),
      collectPages((page) =>
        projectApi.members(s.workspaceId, s.projectId, page, signal),
      ),
    ])
    return [
      ...new Map(
        [
          ...workspace.filter((m) => m.role === 'Owner' || m.role === 'Admin'),
          ...project,
        ].map((member) => [member.userId, member]),
      ).values(),
    ]
  },
  relations: async (s: Scope, signal?: AbortSignal): Promise<WorkItem[]> => {
    const sets = await Promise.all(
      (['false', 'true'] as const).map((archived) =>
        collectPages((page) =>
          workApi.list(
            s,
            { archived, page, pageSize: 100, sort: 'key', descending: false },
            signal,
          ),
        ),
      ),
    )
    return [...new Map(sets.flat().map((item) => [item.id, item])).values()]
  },
}
