import { api } from '@/lib/api'
import type { Page } from '@/features/workspaces/api'
import type { Activity, Priority, Scope } from '@/features/work-items/api'

export type Dashboard = {
  projectId: string
  asOf: string
  todayUtc: string
  totalItems: number
  completedItems: number
  completionPercentage: number
  overdueItems: number
  countsByStatus: {
    statusId: string
    name: string
    category: string
    count: number
  }[]
  countsByPriority: { priority: Priority; count: number }[]
  recentActivity: Activity[]
}
export type Workload = {
  assigneeUserId: string | null
  displayName: string
  openItems: number
  estimateMinutes: number
}
export type OverdueItem = {
  id: string
  key: string
  title: string
  type: string
  status: string
  priority: Priority
  assigneeUserId: string | null
  dueDate: string
  estimateMinutes: number | null
}
export const dashboardKey = (s: Scope) =>
  ['phase6', s.workspaceId, s.projectId] as const
const base = (s: Scope) =>
  `/workspaces/${s.workspaceId}/projects/${s.projectId}/dashboard`
export const dashboardApi = {
  overview: (s: Scope, signal?: AbortSignal) =>
    api<Dashboard>(base(s), { signal }, true),
  workload: (s: Scope, page = 1, signal?: AbortSignal) =>
    api<Page<Workload>>(
      `${base(s)}/workload?page=${page}&pageSize=20`,
      { signal },
      true,
    ),
  overdue: (s: Scope, page = 1, signal?: AbortSignal) =>
    api<Page<OverdueItem>>(
      `${base(s)}/overdue?page=${page}&pageSize=20`,
      { signal },
      true,
    ),
}
