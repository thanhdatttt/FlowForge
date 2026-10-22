import { api } from '@/lib/api'
import type { Page } from '@/features/workspaces/api'

export type Notification = {
  id: string
  type: string
  workspaceId: string
  projectId: string | null
  workItemId: string | null
  commentId: string | null
  invitationId: string | null
  resourceUrl: string
  createdAt: string
  readAt: string | null
}
export type ProjectUpdate = {
  eventId: string
  type: string
  workspaceId: string
  projectId: string | null
  entityId: string
  occurredAt: string
}
export const notificationKey = ['phase4', 'notifications'] as const
export const notificationApi = {
  list: (page = 1, unreadOnly = false, pageSize = 20, signal?: AbortSignal) =>
    api<Page<Notification>>(`/notifications?page=${page}&pageSize=${pageSize}&unreadOnly=${unreadOnly}`, { signal }, true),
  read: (id: string) =>
    api<void>(`/notifications/${id}/read`, { method: 'POST' }, true),
  readAll: () => api<void>('/notifications/read-all', { method: 'POST' }, true),
}
export function notificationLabel(type: string) {
  return ({
    Assigned: 'A work item was assigned to you',
    Mentioned: 'You were mentioned in a comment',
    Overdue: 'An assigned work item is overdue',
    Invited: 'You were invited to a workspace',
    Workflow: 'A workflow sent you a notification',
  } as Record<string, string>)[type] || 'You have a new notification'
}
// Never follow arbitrary server URLs or mistake an API URL for a UI route.
export function notificationPath(n: Notification) {
  if (!n.projectId || !n.workItemId) return null
  return `/workspaces/${n.workspaceId}/projects/${n.projectId}/work-items/${n.workItemId}${n.commentId ? '#work-comments' : ''}`
}
