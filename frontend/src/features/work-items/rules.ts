import type { Role } from '@/features/workspaces/api'
import type {
  ItemType,
  Priority,
  WorkItem,
  WorkItemInput,
  TimeEntry,
  TimeInput,
} from './api'

export const priorities: Priority[] = ['Low', 'Medium', 'High', 'Critical']
export const itemTypes: ItemType[] = ['Task', 'Issue']
export const canWriteWork = (
  role: Role,
  projectArchived: boolean,
  itemArchived = false,
) => role !== 'Viewer' && !projectArchived && !itemArchived
export const canModerate = (role: Role, actorId: string, authorId: string) =>
  actorId === authorId || ['Owner', 'Admin', 'Manager'].includes(role)
export const itemInput = (item: WorkItem): WorkItemInput => ({
  title: item.title,
  description: item.description,
  type: item.type,
  status: item.status,
  priority: item.priority,
  assigneeUserId: item.assigneeUserId,
  parentId: item.parentId,
  startDate: item.startDate,
  dueDate: item.dueDate,
  estimateMinutes: item.estimateMinutes,
  labelIds: [...item.labelIds],
})
export const workPath = (
  workspaceId: string,
  projectId: string,
  itemId?: string,
) =>
  `/workspaces/${workspaceId}/projects/${projectId}/work-items${itemId ? `/${itemId}` : ''}`
export function normalizeSearch(search: string, projectKey: string) {
  const prefix = `${projectKey}-`
  const value = search.trim()
  return value.toUpperCase().startsWith(prefix.toUpperCase()) &&
    /^\d+$/.test(value.slice(prefix.length))
    ? value.slice(prefix.length).replace(/^0+(?=\d)/, '')
    : value
}
// Exclude known descendants; the server remains authoritative for concurrent edits/cycles.
export function parentChoices(items: WorkItem[], itemId?: string) {
  const forbidden = new Set(itemId ? [itemId] : [])
  let changed = true
  while (changed) {
    changed = false
    for (const item of items)
      if (
        item.parentId &&
        forbidden.has(item.parentId) &&
        !forbidden.has(item.id)
      ) {
        forbidden.add(item.id)
        changed = true
      }
  }
  return items.filter((item) => !item.archivedAt && !forbidden.has(item.id))
}
export function localDateTime(value: string | null) {
  if (!value) return ''
  const date = new Date(value)
  return new Date(date.getTime() - date.getTimezoneOffset() * 60_000)
    .toISOString()
    .slice(0, -1)
}
export const utcDateTime = (value: string) =>
  value ? new Date(value).toISOString() : null
const canonicalLocalTime = (value: string) =>
  (value.length === 16 ? `${value}:00` : value)
    .replace(/(\.\d*?[1-9])0+$/, '$1')
    .replace(/\.0+$/, '')
    .replace(/:00$/, '')
export function timeInput(data: FormData, original?: TimeEntry): TimeInput {
  const startedAt = String(data.get('startedAt') || '').trim()
  return {
    minutes: Number(data.get('minutes')),
    // Native datetime-local inputs normalize seconds/fractions. Preserve the exact
    // server instant (including sub-millisecond precision and DST offset) when unchanged.
    startedAt:
      original &&
      canonicalLocalTime(startedAt) ===
        canonicalLocalTime(localDateTime(original.startedAt))
        ? original.startedAt
        : utcDateTime(startedAt),
    note: String(data.get('note') || '').trim() || null,
  }
}
export const timestamp = (value: string) => new Date(value).toLocaleString()
export function mentionParts(body: string) {
  return body.split(/(@\{[0-9a-fA-F-]{36}\})/g).map((text) => {
    const match = /^@\{([0-9a-fA-F-]{36})\}$/.exec(text)
    return { text, userId: match?.[1] }
  })
}
export function validateFile(file: File) {
  if (file.size < 1 || file.size > 10 * 1024 * 1024)
    return 'Choose a file between 1 byte and 10 MiB.'
  if (
    !['image/png', 'image/jpeg', 'application/pdf', 'text/plain'].includes(
      file.type,
    )
  )
    return 'Choose a PNG, JPEG, PDF or plain text file.'
  return null
}
