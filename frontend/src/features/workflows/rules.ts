import type { Role } from '@/features/workspaces/api'
import type { Action, Condition, Definition } from './api'

export const triggers = [
  'WorkItemCreated',
  'WorkItemUpdated',
  'WorkItemStatusChanged',
  'WorkItemAssignmentChanged',
  'CommentAdded',
  'WorkItemDueDateReached',
  'WorkItemDueDateOverdue',
]
export const fields = [
  'Priority',
  'Status',
  'Type',
  'Assignee',
  'Actor',
  'Label',
  'DueDate',
  'Title',
  'Description',
]
export const actionTypes = [
  'Assign',
  'Unassign',
  'ChangeStatus',
  'ChangePriority',
  'AddLabel',
  'RemoveLabel',
  'AddComment',
  'Notify',
  'SendEmail',
]
export const priorities = ['Low', 'Medium', 'High', 'Critical']
export const human = (value: string) =>
  value.replace(/([a-z])([A-Z])/g, '$1 $2')
export const canBrowseWorkflows = (role: Role) =>
  ['Owner', 'Admin', 'Manager'].includes(role)
export const canManageWorkflow = (
  role: Role,
  projectId: string | null,
  assigned: boolean,
) =>
  role === 'Owner' ||
  role === 'Admin' ||
  (role === 'Manager' && !!projectId && assigned)
export const emptyOperator = (operator: string) =>
  ['IsEmpty', 'IsNotEmpty'].includes(operator)
export const operators = (field: string) => [
  'Equals',
  'NotEquals',
  'In',
  'IsEmpty',
  'IsNotEmpty',
  ...(['Priority', 'DueDate'].includes(field)
    ? ['GreaterThan', 'LessThan', 'GreaterThanOrEqual', 'LessThanOrEqual']
    : []),
]
export const recipientAction = (type: string) =>
  ['Assign', 'Notify', 'SendEmail'].includes(type)
export const valueAction = (type: string) =>
  [
    'ChangeStatus',
    'ChangePriority',
    'AddLabel',
    'RemoveLabel',
    'AddComment',
  ].includes(type)
export function newAction(
  type = 'Unassign',
  id: string = crypto.randomUUID(),
): Action {
  return {
    id,
    type,
    ...(recipientAction(type) ? { userId: '' } : {}),
    ...(valueAction(type) ? { value: '' } : {}),
  }
}
export function changeOperator(
  condition: Condition,
  operator: string,
): Condition {
  return {
    ...condition,
    operator,
    values: emptyOperator(operator)
      ? []
      : operator === 'In'
        ? condition.values.length
          ? condition.values
          : ['']
        : [condition.values[0] || ''],
  }
}
export function move<T>(items: T[], index: number, direction: number): T[] {
  const next = [...items]
  const target = index + direction
  if (target < 0 || target >= items.length) return next
  ;[next[index], next[target]] = [next[target], next[index]]
  return next
}
export function cleanDefinition(definition: Definition): Definition {
  return {
    trigger: definition.trigger,
    conditions: definition.conditions.map((c) => ({
      ...c,
      values: emptyOperator(c.operator) ? [] : [...c.values],
    })),
    actions: definition.actions.map((a) => ({
      id: a.id,
      type: a.type,
      ...(recipientAction(a.type) ? { userId: a.userId } : {}),
      ...(valueAction(a.type) ? { value: a.value } : {}),
    })),
  }
}
export const workflowPath = (w: string, id?: string) =>
  `/workspaces/${w}/workflows${id ? `/${id}` : ''}`
export const executionPath = (w: string, id: string) =>
  `/workspaces/${w}/workflow-executions/${id}`
export const conditionOutcome = (value: boolean | null) =>
  value === false
    ? 'Not matched — actions skipped'
    : value === true
      ? 'Matched'
      : 'Not evaluated yet'
