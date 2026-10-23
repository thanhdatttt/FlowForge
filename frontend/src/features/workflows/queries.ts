import { useQuery } from '@tanstack/react-query'
import { projectApi } from '@/features/workspaces/api'
import { collectPages } from '@/features/work-items/api'
import { workflowKey } from './api'

export function useWorkflowProjects(workspaceId: string) {
  return useQuery({
    queryKey: ['phase2', workspaceId, 'workflow-projects'],
    queryFn: ({ signal }) =>
      collectPages((page) =>
        projectApi.list(
          workspaceId,
          {
            search: '',
            archived: '',
            leadUserId: '',
            sort: 'name',
            descending: false,
            page,
          },
          signal,
        ),
      ),
    retry: false,
  })
}
export const workflowInvalidation = (workspaceId: string) => [
  workflowKey(workspaceId),
  ['phase2', workspaceId],
  ['phase3', workspaceId],
]
