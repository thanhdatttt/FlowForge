import { useQuery } from '@tanstack/react-query'
import { workApi, type Scope } from './api'

export const workKey = (s: Scope) =>
  ['phase3', s.workspaceId, s.projectId] as const
export const invalidateKeys = (s: Scope) => [
  workKey(s),
  ['phase2', s.workspaceId],
]
export function useMembers(scope: Scope) {
  return useQuery({
    queryKey: [
      'phase2',
      scope.workspaceId,
      'project',
      scope.projectId,
      'work-members',
    ],
    queryFn: ({ signal }) => workApi.members(scope, signal),
    retry: false,
  })
}
export function useLabels(scope: Scope) {
  return useQuery({
    queryKey: [...workKey(scope), 'labels'],
    queryFn: ({ signal }) => workApi.labels(scope, signal),
    retry: false,
  })
}
export function useRelations(scope: Scope) {
  return useQuery({
    queryKey: [...workKey(scope), 'relations'],
    queryFn: ({ signal }) => workApi.relations(scope, signal),
    retry: false,
  })
}
