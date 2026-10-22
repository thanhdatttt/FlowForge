import { useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { Empty, Pager, QueryState } from '@/features/workspaces/shared'
import { workApi, type Scope } from './api'
import { timestamp } from './rules'
import { useMembers, workKey } from './queries'

function Changes({
  values,
  title,
}: {
  values: Record<string, unknown> | null
  title: string
}) {
  if (!values) return null
  return (
    <div>
      <h3>{title}</h3>
      <dl className="work-changes">
        {Object.entries(values).map(([key, value]) => (
          <div key={key}>
            <dt>{key.replace(/([a-z])([A-Z])/g, '$1 $2')}</dt>
            <dd>
              {value === null
                ? 'Not set'
                : typeof value === 'object'
                  ? JSON.stringify(value, null, 2)
                  : String(value)}
            </dd>
          </div>
        ))}
      </dl>
    </div>
  )
}
export function ActivityFeed({
  scope,
  itemId,
}: {
  scope: Scope
  itemId?: string
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
                {(record.beforeValues || record.afterValues) && (
                  <details>
                    <summary>View changes</summary>
                    <div className="work-change-columns">
                      <Changes title="Before" values={record.beforeValues} />
                      <Changes title="After" values={record.afterValues} />
                    </div>
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
