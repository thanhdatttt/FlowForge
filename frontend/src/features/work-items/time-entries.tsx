import { useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { FormField } from '@/components/ui/form-field'
import { Empty, Modal, Pager, QueryState } from '@/features/workspaces/shared'
import type { Role } from '@/features/workspaces/api'
import { workApi, type Scope, type TimeEntry } from './api'
import { canModerate, localDateTime, timestamp, timeInput } from './rules'
import { TextArea, WorkConfirm, WorkForm } from './shared'
import { useMembers, workKey } from './queries'

function TimeEditor({
  scope,
  itemId,
  entry: current,
  close,
}: {
  scope: Scope
  itemId: string
  entry?: TimeEntry
  close: () => void
}) {
  const [entry] = useState(current)
  return (
    <WorkForm
      scope={scope}
      label={entry ? 'Save time entry' : 'Log time'}
      done={close}
      submit={(data) =>
        workApi.saveTime(scope, itemId, timeInput(data, entry), entry?.id)
      }
    >
      {(error) => (
        <>
          <FormField
            label="Time spent (minutes)"
            name="minutes"
            type="number"
            required
            min={1}
            max={1000000}
            step={1}
            defaultValue={entry?.minutes || ''}
            error={error('minutes')}
          />
          <FormField
            label="Started at (your local time, optional)"
            name="startedAt"
            type="datetime-local"
            step="any"
            defaultValue={localDateTime(entry?.startedAt || null)}
            error={error('startedAt')}
          />
          <TextArea
            label="Note (optional)"
            name="note"
            maxLength={2000}
            rows={3}
            defaultValue={entry?.note || ''}
            error={error('note')}
          />
        </>
      )}
    </WorkForm>
  )
}
export function TimeEntries({
  scope,
  itemId,
  userId,
  role,
  writable,
}: {
  scope: Scope
  itemId: string
  userId: string
  role: Role
  writable: boolean
}) {
  const [page, setPage] = useState(1)
  const members = useMembers(scope)
  const entries = useQuery({
    queryKey: [...workKey(scope), 'time', itemId, page],
    queryFn: ({ signal }) => workApi.timeEntries(scope, itemId, page, signal),
    retry: false,
  })
  return (
    <section className="workspace-card" id="work-time">
      <div className="workspace-section-heading">
        <h2>Time entries</h2>
        {writable && (
          <Modal title="Log time" trigger="Log time">
            {(close) => (
              <TimeEditor scope={scope} itemId={itemId} close={close} />
            )}
          </Modal>
        )}
      </div>
      <QueryState
        pending={entries.isPending}
        hasData={!!entries.data}
        error={entries.error}
        retry={entries.refetch}
      >
        {!entries.data?.items.length && <Empty>No time logged yet.</Empty>}
        <ul className="workspace-rows">
          {entries.data?.items.map((entry) => (
            <li key={entry.id}>
              <div className="work-entry-main">
                <strong>{entry.minutes.toLocaleString()} minutes</strong>
                <p className="muted">
                  {members.data?.find((m) => m.userId === entry.userId)
                    ?.displayName ||
                    `Previous or unavailable member (${entry.userId})`}{' '}
                  ·{' '}
                  {entry.startedAt
                    ? `Started ${timestamp(entry.startedAt)}`
                    : `Logged ${timestamp(entry.createdAt)}`}
                </p>
                {entry.note && (
                  <p className="workspace-description">{entry.note}</p>
                )}
              </div>
              {writable && canModerate(role, userId, entry.userId) && (
                <div className="workspace-actions">
                  <Modal title="Edit time entry" trigger="Edit time entry">
                    {(close) => (
                      <TimeEditor
                        scope={scope}
                        itemId={itemId}
                        entry={entry}
                        close={close}
                      />
                    )}
                  </Modal>
                  <WorkConfirm
                    scope={scope}
                    title="Delete time entry?"
                    description="This removes the entry. Its history is preserved in activity."
                    label="Delete time entry"
                    run={() => workApi.deleteTime(scope, itemId, entry.id)}
                  />
                </div>
              )}
            </li>
          ))}
        </ul>
        {entries.data && (
          <Pager page={page} total={entries.data.total} change={setPage} />
        )}
      </QueryState>
    </section>
  )
}
