import { useQuery } from '@tanstack/react-query'
import { projectApi, type Workspace } from '@/features/workspaces/api'
import { ProjectDetail } from '@/features/workspaces/projects'
import { QueryState } from '@/features/workspaces/shared'
import type { Route } from '@/features/workspaces/rules'
import { WorkLink } from './shared'

import { WorkList } from './list'
import { WorkDetail } from './detail'
import { Labels } from './labels'
import { ActivityFeed } from './activity'

export function ProjectWork({
  workspace,
  route,
  userId,
  navigate,
}: {
  workspace: Workspace
  route: Route
  userId: string
  navigate: (path: string) => void
}) {
  const projectId = route.projectId!
  const result = useQuery({
    queryKey: ['phase2', workspace.id, 'project', projectId],
    queryFn: ({ signal }) => projectApi.get(workspace.id, projectId, signal),
    retry: false,
  })
  const project = result.data
  const base = `/workspaces/${workspace.id}/projects/${projectId}`
  return (
    <QueryState
      pending={result.isPending}
      hasData={!!result.data}
      error={result.error}
      retry={result.refetch}
    >
      {project && (
        <div className="workspace-stack">
          <div className="work-project-header">
            <WorkLink
              className="text-link"
              href={`/workspaces/${workspace.id}/projects`}
              navigate={navigate}
            >
              ← Projects
            </WorkLink>
            <p>
              <span className="workspace-key">{project.projectKey}</span>{' '}
              <strong>{project.name}</strong>
            </p>
          </div>
          <nav className="work-project-nav" aria-label="Project navigation">
            {[
              ['', 'Overview'],
              ['/work-items', 'Work items'],
              ['/labels', 'Labels'],
              ['/activity', 'Activity'],
            ].map(([suffix, label]) => (
              <WorkLink
                key={suffix}
                href={`${base}${suffix}`}
                navigate={navigate}
                aria-current={
                  (route.projectSection || '') === suffix.slice(1)
                    ? 'page'
                    : undefined
                }
              >
                {label}
              </WorkLink>
            ))}
          </nav>
          {project.archivedAt && (
            <p className="workspace-readonly" role="status">
              This project is archived. Work items and collaboration are read
              only.
            </p>
          )}
          {route.projectSection === 'work-items' ? (
            route.itemId ? (
              <WorkDetail
                key={route.itemId}
                workspace={workspace}
                project={project}
                itemId={route.itemId}
                userId={userId}
                navigate={navigate}
              />
            ) : (
              <WorkList
                key={projectId}
                workspace={workspace}
                project={project}
                navigate={navigate}
              />
            )
          ) : route.projectSection === 'labels' ? (
            <Labels workspace={workspace} project={project} />
          ) : route.projectSection === 'activity' ? (
            <ActivityFeed scope={{ workspaceId: workspace.id, projectId }} />
          ) : (
            <ProjectDetail workspace={workspace} projectId={projectId} />
          )}
        </div>
      )}
    </QueryState>
  )
}
