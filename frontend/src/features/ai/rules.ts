import { ApiError } from '@/lib/api'
import type { Proposal } from './api'

// Keys match the compact, selected task list sent to the server.
export function validateProposals(tasks: Proposal[]): Record<string, string> {
  const errors: Record<string, string> = {}
  if (tasks.length < 1 || tasks.length > 20)
    return { tasks: 'Select between 1 and 20 tasks.' }
  const ids = new Set<string>()
  tasks.forEach((task, i) => {
    if (!/^[A-Za-z][A-Za-z0-9_-]{0,39}$/.test(task.id) || ids.has(task.id))
      errors[`tasks[${i}].id`] = 'Each task needs a unique proposal ID.'
    ids.add(task.id)
    if (!task.title.trim() || task.title.trim().length > 200)
      errors[`tasks[${i}].title`] = 'Enter a title of 1–200 characters.'
    if ((task.description?.length || 0) > 20_000)
      errors[`tasks[${i}].description`] = 'Use at most 20,000 characters.'
    if (!['Low', 'Medium', 'High', 'Critical'].includes(task.priority))
      errors[`tasks[${i}].priority`] = 'Choose a valid priority.'
    if (
      task.dependsOn.length > 19 ||
      new Set(task.dependsOn).size !== task.dependsOn.length
    )
      errors[`tasks[${i}].dependsOn`] = 'Choose at most 19 unique dependencies.'
  })
  tasks.forEach((task, i) => {
    if (task.dependsOn.some((id) => id === task.id || !ids.has(id)))
      errors[`tasks[${i}].dependsOn`] =
        'Dependencies must reference other selected tasks. Remove the dependency or select its task.'
  })
  if (Object.keys(errors).length) return errors
  const byId = new Map(tasks.map((t) => [t.id, t]))
  const visiting = new Set<string>(),
    visited = new Set<string>()
  const cycle = (id: string): boolean => {
    if (visited.has(id)) return false
    if (visiting.has(id)) return true
    visiting.add(id)
    if (byId.get(id)!.dependsOn.some(cycle)) return true
    visiting.delete(id)
    visited.add(id)
    return false
  }
  if (tasks.some((task) => cycle(task.id)))
    errors.tasks = 'Dependency cycles are not allowed.'
  return errors
}

export function aiFailure(
  error: unknown,
  operation: 'generate' | 'accept' = 'generate',
): string {
  if (!(error instanceof ApiError))
    return error instanceof Error ? error.message : 'The request failed.'
  if (operation === 'accept' && (error.status === 0 || error.status >= 500))
    return 'The confirmation response was not received. Retry the same confirmation to check whether the tasks were created.'
  switch (error.status) {
    case 400:
      return 'Check the highlighted fields and preview token.'
    case 403:
      return 'You no longer have permission for this action. Refresh your project access.'
    case 404:
      return 'The project or preview is unavailable, or you no longer have access.'
    case 409:
      return 'The project is archived, its state changed, or this preview was already accepted with different content. Refresh before continuing.'
    case 410:
      return operation === 'accept'
        ? 'This preview has expired. Check Work items for any previously confirmed tasks before starting another breakdown.'
        : 'This preview has expired. Generate a new proposal to continue.'
    case 429:
      return `AI request limit reached. ${error.retryAfterSeconds ? `Wait ${error.retryAfterSeconds} seconds` : 'Wait a minute'} before trying again.`
    case 502:
      return 'AI returned an invalid response. Try generating again.'
    case 503:
      return 'AI is unavailable or has not been configured by the server administrator. Project features remain available.'
    case 504:
      return 'AI took too long to respond. Try again when you are ready.'
    default:
      return error.message
  }
}
