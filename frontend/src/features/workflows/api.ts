import { api } from '@/lib/api'
import type { Page } from '@/features/workspaces/api'

export type Condition = { field: string; operator: string; values: string[] }
export type Action = {
  id: string
  type: string
  userId?: string | null
  value?: string | null
}
export type Definition = {
  trigger: string
  conditions: Condition[]
  actions: Action[]
}
export type WorkflowInput = {
  name: string
  projectId: string | null
  definition: Definition
  version?: number
}
export type Workflow = WorkflowInput & {
  id: string
  workspaceId: string
  createdByUserId: string
  isEnabled: boolean
  version: number
  createdAt: string
  updatedAt: string
}
export type Execution = {
  id: string
  workflowId: string
  workflowVersionId: string
  eventId: string
  projectId: string
  workItemId: string
  status: string
  conditionsMatched: boolean | null
  attemptCount: number
  createdAt: string
  startedAt: string | null
  finishedAt: string | null
  safeError: string | null
  version: number
  definition: Definition
  correlationId: string
  actorUserId: string | null
  originExecutionId: string | null
  depth: number
  actions: {
    actionId: string
    status: string
    attemptCount: number
    finishedAt: string | null
    safeError: string | null
  }[]
  emailDeliveries: {
    id: string
    recipientUserId: string | null
    status: string
    attemptCount: number
    completedAt: string | null
    safeError: string | null
  }[]
}
const base = (workspaceId: string) => `/workspaces/${workspaceId}/workflows`
const read = <T>(path: string, signal?: AbortSignal) =>
  api<T>(path, { signal }, true)
const send = <T>(path: string, method: string, body: unknown) =>
  api<T>(path, { method, body: JSON.stringify(body) }, true)
export const workflowKey = (workspaceId: string) =>
  ['phase5', workspaceId] as const
export const workflowApi = {
  list: (
    workspaceId: string,
    page = 1,
    projectId?: string,
    signal?: AbortSignal,
  ) => {
    const query = new URLSearchParams({ page: String(page), pageSize: '20' })
    if (projectId) query.set('projectId', projectId)
    return read<Page<Workflow>>(`${base(workspaceId)}?${query}`, signal)
  },
  get: (w: string, id: string, signal?: AbortSignal) =>
    read<Workflow>(`${base(w)}/${id}`, signal),
  create: (w: string, input: WorkflowInput) =>
    send<Workflow>(base(w), 'POST', input),
  validate: (w: string, input: WorkflowInput) =>
    send<{ valid: true }>(`${base(w)}/validate`, 'POST', input),
  update: (w: string, id: string, input: WorkflowInput) =>
    send<Workflow>(`${base(w)}/${id}`, 'PUT', input),
  state: (w: string, id: string, enabled: boolean, version: number) =>
    send<Workflow>(
      `${base(w)}/${id}/${enabled ? 'enable' : 'disable'}`,
      'POST',
      { version },
    ),
  executions: (w: string, id: string, page = 1, signal?: AbortSignal) =>
    read<Page<Execution>>(
      `${base(w)}/${id}/executions?page=${page}&pageSize=20`,
      signal,
    ),
  execution: (
    w: string,
    id: string,
    executionId: string,
    signal?: AbortSignal,
  ) => read<Execution>(`${base(w)}/${id}/executions/${executionId}`, signal),
  lookupExecution: (w: string, executionId: string, signal?: AbortSignal) =>
    read<Execution>(`${base(w)}/executions/${executionId}`, signal),
}
