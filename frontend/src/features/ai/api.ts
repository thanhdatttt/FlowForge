import { api } from '@/lib/api'
import type { Priority, Scope } from '@/features/work-items/api'

export type Proposal = {
  id: string
  title: string
  description: string | null
  priority: Priority
  dependsOn: string[]
}
export type Preview = {
  previewToken: string
  expiresAt: string
  tasks: Proposal[]
  isAiGenerated: boolean
}
export type Acceptance = {
  previewToken: string
  accepted: true
  tasks: Proposal[]
}
export type AcceptedTasks = {
  batchId: string
  tasks: { proposalId: string; id: string; key: string }[]
}
export type ProjectSummary = {
  projectId: string
  asOf: string
  todayUtc: string
  facts: {
    totalItems: number
    completedItems: number
    completionPercentage: number
    overdueItems: number
    blockedItems: number
  }
  status: 'Generated' | 'InsufficientData'
  narrative: {
    overview: string
    completedWork: string
    blockers: string
    overdueWork: string
    risks: string[]
  } | null
  isAiGenerated: boolean
  contextItemCount: number
  contextTruncated: boolean
}
const base = (s: Scope) =>
  `/workspaces/${s.workspaceId}/projects/${s.projectId}/ai`
const post = <T>(path: string, body: unknown, signal?: AbortSignal) =>
  api<T>(
    path,
    {
      method: 'POST',
      ...(body === undefined ? {} : { body: JSON.stringify(body) }),
      // Backend AI permits up to 120 seconds. Cancellation also cancels transport.
      signal: signal
        ? AbortSignal.any([signal, AbortSignal.timeout(125_000)])
        : AbortSignal.timeout(125_000),
    },
    true,
  )
export const aiApi = {
  propose: (s: Scope, featureDescription: string, signal?: AbortSignal) =>
    post<Preview>(`${base(s)}/tasks/propose`, { featureDescription }, signal),
  accept: (s: Scope, request: Acceptance, signal?: AbortSignal) =>
    post<AcceptedTasks>(`${base(s)}/tasks/accept`, request, signal),
  summary: (s: Scope, signal?: AbortSignal) =>
    post<ProjectSummary>(`${base(s)}/summary`, undefined, signal),
}
