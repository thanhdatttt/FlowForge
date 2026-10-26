import { useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { ApiError } from '@/lib/api'
import type { Scope } from '@/features/work-items/api'
import { ActivityChanges } from '@/features/work-items/activity'
import { useMembers } from '@/features/work-items/queries'
import { timestamp, workPath } from '@/features/work-items/rules'
import { WorkLink } from '@/features/work-items/shared'
import { executionPath } from '@/features/workflows/rules'
import { Empty, Pager, QueryState } from '@/features/workspaces/shared'
import { dashboardApi, dashboardKey, type Dashboard } from './api'

const refreshInterval = (query: { state: { error: unknown } }) =>
  query.state.error instanceof ApiError &&
  [401, 403, 404].includes(query.state.error.status)
    ? false
    : 60_000

export function Metrics({
  facts,
}: {
  facts: Pick<
    Dashboard,
    'totalItems' | 'completedItems' | 'completionPercentage' | 'overdueItems'
  >
}) {
  return (
    <dl className="dashboard-metrics">
      {[
        ['Total items', facts.totalItems],
        ['Completed', facts.completedItems],
        ['Completion', `${facts.completionPercentage}%`],
        ['Overdue', facts.overdueItems],
      ].map(([label, value]) => (
        <div key={label}>
          <dt>{label}</dt>
          <dd>{value}</dd>
        </div>
      ))}
    </dl>
  )
}

export function ProjectDashboard({
  scope,
  navigate,
  inspectWorkflows,
}: {
  scope: Scope
  navigate: (path: string) => void
  inspectWorkflows: boolean
}) {
  const [overduePage, setOverduePage] = useState(1)
  const [workloadPage, setWorkloadPage] = useState(1)
  const key = dashboardKey(scope)
  const overview = useQuery({
    queryKey: [...key, 'overview'],
    queryFn: ({ signal }) => dashboardApi.overview(scope, signal),
    retry: false,
    refetchInterval: refreshInterval,
  })
  // Stop child reads after access has been denied. Never display retained private rows then.
  const readable = !!overview.data && !overview.error
  const workload = useQuery({
    queryKey: [...key, 'workload', workloadPage],
    queryFn: ({ signal }) => dashboardApi.workload(scope, workloadPage, signal),
    enabled: readable,
    retry: false,
    refetchInterval: refreshInterval,
  })
  const overdue = useQuery({
    queryKey: [...key, 'overdue', overduePage, overview.data?.todayUtc],
    queryFn: ({ signal }) => dashboardApi.overdue(scope, overduePage, signal),
    enabled: readable,
    retry: false,
    refetchInterval: refreshInterval,
  })
  const members = useMembers(scope)
  const memberName = (id: string | null) =>
    id === null
      ? 'Unassigned'
      : members.data?.find((m) => m.userId === id)?.displayName ||
        `Previous or unavailable member (${id})`
  const base = `/workspaces/${scope.workspaceId}/projects/${scope.projectId}`
  return (
    <div className="workspace-stack">
      <section className="workspace-card">
        <h2>Project dashboard</h2>
        <p className="muted">
          Progress and workload calculated from project work items.
        </p>
        <QueryState
          pending={overview.isPending}
          hasData={!!overview.data}
          error={overview.error}
          retry={overview.refetch}
        >
          {overview.data && (
            <>
              <Metrics facts={overview.data} />
              <label className="dashboard-progress">
                Completion{' '}
                <progress
                  max={100}
                  value={overview.data.completionPercentage}
                />{' '}
                <span>{overview.data.completionPercentage}%</span>
              </label>
              <p className="muted">
                Snapshot{' '}
                <time dateTime={overview.data.asOf}>
                  {timestamp(overview.data.asOf)}
                </time>{' '}
                · Overdue uses UTC date {overview.data.todayUtc}.
              </p>
              {overview.data.totalItems === 0 && (
                <Empty>
                  No active work items yet. Create work items to see project
                  progress.
                </Empty>
              )}
              <p className="muted">
                Tasks, issues and subtasks count individually. Archived items
                are excluded; completed means status category Done.
              </p>
              <div className="dashboard-columns">
                <Breakdown
                  title="By status"
                  rows={overview.data.countsByStatus.map((s) => ({
                    id: s.statusId,
                    label: s.name,
                    count: s.count,
                  }))}
                />
                <Breakdown
                  title="By priority"
                  rows={overview.data.countsByPriority.map((p) => ({
                    id: p.priority,
                    label: p.priority,
                    count: p.count,
                  }))}
                />
              </div>
            </>
          )}
        </QueryState>
      </section>
      {readable && (
        <>
          <section className="workspace-card">
            <h2>Overdue work</h2>
            <p className="muted">
              Unfinished work due before today in UTC, including unassigned
              items.
            </p>
            <QueryState
              pending={overdue.isPending}
              hasData={!!overdue.data}
              error={overdue.error}
              retry={overdue.refetch}
            >
              {!overdue.data?.items.length && (
                <Empty>No overdue work on this page.</Empty>
              )}
              <ul className="workspace-rows">
                {overdue.data?.items.map((item) => (
                  <li key={item.id}>
                    <div>
                      <WorkLink
                        className="text-link dashboard-item-link"
                        href={workPath(
                          scope.workspaceId,
                          scope.projectId,
                          item.id,
                        )}
                        navigate={navigate}
                      >
                        <span className="workspace-key">{item.key}</span>{' '}
                        {item.title}
                      </WorkLink>
                      <p className="muted">
                        {item.type} · {item.status} · {item.priority} · Due{' '}
                        <time dateTime={item.dueDate}>{item.dueDate}</time>
                      </p>
                      <p className="muted">
                        {memberName(item.assigneeUserId)} ·{' '}
                        {item.estimateMinutes === null
                          ? 'No estimate'
                          : `${item.estimateMinutes.toLocaleString()} min estimated`}
                      </p>
                    </div>
                  </li>
                ))}
              </ul>
              {overdue.data && (
                <Pager
                  page={overduePage}
                  total={overdue.data.total}
                  change={setOverduePage}
                />
              )}
            </QueryState>
          </section>
          <section className="workspace-card">
            <h2>Assignee workload</h2>
            <p className="muted">
              Open work and total estimated minutes. Historical assignment
              attribution is retained.
            </p>
            <QueryState
              pending={workload.isPending}
              hasData={!!workload.data}
              error={workload.error}
              retry={workload.refetch}
            >
              {!workload.data?.items.length && (
                <Empty>No open workload on this page.</Empty>
              )}
              <ul className="workspace-rows">
                {workload.data?.items.map((row) => (
                  <li key={row.assigneeUserId || 'unassigned'}>
                    <div>
                      <strong>{row.displayName}</strong>
                      <p className="muted">
                        {row.openItems.toLocaleString()} open items ·{' '}
                        {row.estimateMinutes.toLocaleString()} min estimated
                      </p>
                    </div>
                  </li>
                ))}
              </ul>
              {workload.data && (
                <Pager
                  page={workloadPage}
                  total={workload.data.total}
                  change={setWorkloadPage}
                />
              )}
            </QueryState>
          </section>
          <section className="workspace-card">
            <div className="workspace-section-heading">
              <h2>Recent activity</h2>
              <WorkLink
                className="text-link"
                href={`${base}/activity`}
                navigate={navigate}
              >
                View all activity
              </WorkLink>
            </div>
            {!overview.data!.recentActivity.length && (
              <Empty>No activity yet.</Empty>
            )}
            <ol className="workspace-rows work-activity">
              {overview.data!.recentActivity.map((record) => (
                <li key={record.id}>
                  <div>
                    <strong>
                      {record.action
                        .replaceAll('.', ' · ')
                        .replaceAll('-', ' ')}
                    </strong>
                    <p className="muted">
                      {record.actorUserId
                        ? memberName(record.actorUserId)
                        : record.actorKind}{' '}
                      ·{' '}
                      <time dateTime={record.occurredAt}>
                        {timestamp(record.occurredAt)}
                      </time>
                    </p>
                    {record.workItemId && (
                      <WorkLink
                        className="text-link"
                        href={workPath(
                          scope.workspaceId,
                          scope.projectId,
                          record.workItemId,
                        )}
                        navigate={navigate}
                      >
                        View work item
                      </WorkLink>
                    )}
                    {record.workflowExecutionId && (
                      <p>
                        {inspectWorkflows ? (
                          <WorkLink
                            className="text-link"
                            href={executionPath(
                              scope.workspaceId,
                              record.workflowExecutionId,
                            )}
                            navigate={navigate}
                          >
                            View workflow execution
                          </WorkLink>
                        ) : (
                          'Workflow action'
                        )}
                      </p>
                    )}
                    {(record.beforeValues || record.afterValues) && (
                      <details>
                        <summary>View changes</summary>
                        <ActivityChanges
                          before={record.beforeValues}
                          after={record.afterValues}
                        />
                      </details>
                    )}
                  </div>
                </li>
              ))}
            </ol>
          </section>
        </>
      )}
    </div>
  )
}

function Breakdown({
  title,
  rows,
}: {
  title: string
  rows: { id: string; label: string; count: number }[]
}) {
  return (
    <section>
      <h3>{title}</h3>
      <dl className="dashboard-breakdown">
        {rows.map((row) => (
          <div key={row.id}>
            <dt>{row.label}</dt>
            <dd>{row.count.toLocaleString()}</dd>
          </div>
        ))}
      </dl>
    </section>
  )
}
