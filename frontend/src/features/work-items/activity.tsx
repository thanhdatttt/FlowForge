import { WorkLink } from './shared'
import { executionPath } from '@/features/workflows/rules'
import { useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { Empty, Pager, QueryState } from '@/features/workspaces/shared'
import { workApi, type Scope } from './api'
import { timestamp } from './rules'
import { useMembers, workKey } from './queries'

function changeValue(values: Record<string, unknown> | null, key: string) {
  if (!values || !Object.hasOwn(values, key))
    return <span className="work-change-missing">Not recorded</span>
  const value = values[key]
  if (value === null)
    return <span className="work-change-missing">Not set</span>
  if (value === '')
    return <span className="work-change-missing">Empty text</span>
  return typeof value === 'object'
    ? JSON.stringify(value, null, 2)
    : String(value)
}
export function ActivityChanges({
  before,
  after,
}: {
  before: Record<string, unknown> | null
  after: Record<string, unknown> | null
}) {
  const keys = [
    ...new Set([...Object.keys(before || {}), ...Object.keys(after || {})]),
  ]
  return (
    <div className="work-change-comparison">
      <table className="work-changes">
        <caption className="sr-only">
          Activity values before and after the change
        </caption>
        <thead>
          <tr>
            <th scope="col">Field</th>
            <th scope="col">Before</th>
            <th scope="col">After</th>
          </tr>
        </thead>
        <tbody>
          {keys.map((key) => (
            <tr key={key}>
              <th scope="row">
                {key
                  .replace(/([a-z])([A-Z])/g, '$1 $2')
                  .replace(/\bId\b/g, 'ID')
                  .replace(/\bIds\b/g, 'IDs')}
              </th>
              <td>
                <div>{changeValue(before, key)}</div>
              </td>
              <td>
                <div>{changeValue(after, key)}</div>
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  )
}
export function ActivityFeed({
  scope,
  itemId,
  navigate,
  inspectWorkflows = false,
}: {
  scope: Scope
  itemId?: string
  navigate: (path: string) => void
  inspectWorkflows?: boolean
}) {
  const [page, setPage] = useState(1)
  const members = useMembers(scope)
  const activity = useQuery({
    queryKey: [...workKey(scope), 'activity', itemId, page],
    queryFn: ({ signal }) => workApi.activity(scope, page, itemId, signal),
    retry: false,
  })
  return (
    <section className="workspace-card" id="work-activity">
      <h2>Activity</h2>
      <p className="muted">
        Changes and collaboration history
        {itemId ? ' for this work item' : ' across this project'}.
      </p>
      <QueryState
        pending={activity.isPending}
        hasData={!!activity.data}
        error={activity.error}
        retry={activity.refetch}
      >
        {!activity.data?.items.length && <Empty>No activity yet.</Empty>}
        <ol className="workspace-rows work-activity">
          {activity.data?.items.map((record) => (
            <li key={record.id}>
              <div>
                <strong>
                  {record.action.replaceAll('.', ' · ').replaceAll('-', ' ')}
                </strong>
                <p className="muted">
                  {record.actorUserId
                    ? members.data?.find((m) => m.userId === record.actorUserId)
                        ?.displayName ||
                      `Previous or unavailable member (${record.actorUserId})`
                    : record.actorKind}{' '}
                  ·{' '}
                  <time dateTime={record.occurredAt}>
                    {timestamp(record.occurredAt)}
                  </time>
                </p>
                {record.workflowExecutionId && (
                  <p className="muted">
                    Automation ·{' '}
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
        {activity.data && (
          <Pager page={page} total={activity.data.total} change={setPage} />
        )}
      </QueryState>
    </section>
  )
}
