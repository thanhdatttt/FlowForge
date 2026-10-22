import { useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { Button } from '@/components/ui/button'
import { FormField } from '@/components/ui/form-field'
import {
  Empty,
  Modal,
  Pager,
  QueryState,
  SelectField,
} from '@/features/workspaces/shared'
import type { Project, Workspace } from '@/features/workspaces/api'
import { workApi, type ItemFilters, type Scope } from './api'
import {
  canWriteWork,
  itemTypes,
  normalizeSearch,
  priorities,
  workPath,
} from './rules'
import { ItemEditor } from './editor'
import { ItemBadges, WorkLink } from './shared'
import { useLabels, useMembers, workKey } from './queries'

export function WorkList({
  workspace,
  project,
  navigate,
}: {
  workspace: Workspace
  project: Project
  navigate: (path: string) => void
}) {
  const scope: Scope = { workspaceId: workspace.id, projectId: project.id }
  const [filters, setFilters] = useState<ItemFilters>({
    page: 1,
    sort: 'updatedAt',
    descending: true,
    archived: 'false',
  })
  const [search, setSearch] = useState('')
  const change = (next: ItemFilters) =>
    setFilters((old) => ({ ...old, page: 1, ...next }))
  const statuses = useQuery({
    queryKey: [...workKey(scope), 'statuses'],
    queryFn: ({ signal }) => workApi.statuses(scope, signal),
    retry: false,
  })
  const labels = useLabels(scope)
  const members = useMembers(scope)
  const items = useQuery({
    queryKey: [...workKey(scope), 'list', filters],
    queryFn: ({ signal }) => workApi.list(scope, filters, signal),
    retry: false,
  })
  return (
    <section className="workspace-card">
      <div className="workspace-section-heading">
        <div>
          <h2>Work items</h2>
          <p className="muted">
            Tasks, issues and the work that moves this project forward.
          </p>
        </div>
        {canWriteWork(workspace.role, !!project.archivedAt) && (
          <Modal title="New work item" trigger="New work item">
            {(close) => (
              <ItemEditor
                scope={scope}
                saved={(item) => {
                  close()
                  navigate(workPath(workspace.id, project.id, item.id))
                }}
              />
            )}
          </Modal>
        )}
      </div>
      <form
        className="work-filter-form"
        onSubmit={(event) => {
          event.preventDefault()
          change({ search: normalizeSearch(search, project.projectKey) })
        }}
      >
        <div className="workspace-filters">
          <FormField
            label="Search work items"
            value={search}
            maxLength={200}
            placeholder="Title, number or project key"
            onChange={(event) => setSearch(event.target.value)}
          />
          <Button type="submit" variant="secondary">
            Search
          </Button>
          <SelectField
            label="Status"
            value={filters.status || ''}
            disabled={!statuses.data}
            onChange={(e) => change({ status: e.target.value })}
          >
            <option value="">All statuses</option>
            {statuses.data?.map((s) => (
              <option key={s.id} value={s.name}>
                {s.name.replace(/([a-z])([A-Z])/g, '$1 $2')}
              </option>
            ))}
          </SelectField>
          <SelectField
            label="Priority"
            value={filters.priority || ''}
            onChange={(e) => change({ priority: e.target.value })}
          >
            <option value="">All priorities</option>
            {priorities.map((p) => (
              <option key={p}>{p}</option>
            ))}
          </SelectField>
          <SelectField
            label="Assignee"
            value={filters.assigneeUserId || ''}
            disabled={!members.data}
            onChange={(e) => change({ assigneeUserId: e.target.value })}
          >
            <option value="">All assignees</option>
            {members.data?.map((m) => (
              <option key={m.userId} value={m.userId}>
                {m.displayName}
              </option>
            ))}
          </SelectField>
        </div>
        <details className="work-more-filters">
          <summary>More filters and sorting</summary>
          <div className="workspace-filters">
            <SelectField
              label="Type"
              value={filters.type || ''}
              onChange={(e) => change({ type: e.target.value })}
            >
              <option value="">Tasks and issues</option>
              {itemTypes.map((t) => (
                <option key={t}>{t}</option>
              ))}
            </SelectField>
            <SelectField
              label="Reporter"
              value={filters.reporterUserId || ''}
              disabled={!members.data}
              onChange={(e) => change({ reporterUserId: e.target.value })}
            >
              <option value="">All reporters</option>
              {members.data?.map((m) => (
                <option key={m.userId} value={m.userId}>
                  {m.displayName}
                </option>
              ))}
            </SelectField>
            <SelectField
              label="Label"
              value={filters.labelId || ''}
              disabled={!labels.data}
              onChange={(e) => change({ labelId: e.target.value })}
            >
              <option value="">All labels</option>
              {labels.data?.map((l) => (
                <option key={l.id} value={l.id}>
                  {l.name}
                </option>
              ))}
            </SelectField>
            <FormField
              label="Due on or after"
              type="date"
              value={filters.dueAfter || ''}
              onChange={(e) => change({ dueAfter: e.target.value })}
            />
            <FormField
              label="Due on or before"
              type="date"
              min={filters.dueAfter || undefined}
              value={filters.dueBefore || ''}
              onChange={(e) => change({ dueBefore: e.target.value })}
            />
            <SelectField
              label="Archive state"
              value={filters.archived}
              onChange={(e) =>
                change({ archived: e.target.value as 'true' | 'false' })
              }
            >
              <option value="false">Active</option>
              <option value="true">Archived</option>
            </SelectField>
            <SelectField
              label="Sort by"
              value={filters.sort}
              onChange={(e) => change({ sort: e.target.value })}
            >
              {['updatedAt', 'createdAt', 'dueDate', 'priority', 'key'].map(
                (sort, i) => (
                  <option key={sort} value={sort}>
                    {['Updated', 'Created', 'Due date', 'Priority', 'Key'][i]}
                  </option>
                ),
              )}
            </SelectField>
            <SelectField
              label="Order"
              value={String(filters.descending)}
              onChange={(e) =>
                change({ descending: e.target.value === 'true' })
              }
            >
              <option value="true">Descending</option>
              <option value="false">Ascending</option>
            </SelectField>
          </div>
        </details>
        <Button
          type="button"
          variant="text"
          onClick={() => {
            setSearch('')
            setFilters({
              page: 1,
              sort: 'updatedAt',
              descending: true,
              archived: 'false',
            })
          }}
        >
          Reset filters
        </Button>
      </form>
      {[statuses, labels, members].some((q) => q.isError) && (
        <div className="workspace-readonly" role="status">
          Some filter options are unavailable.{' '}
          <button
            className="text-link"
            onClick={() =>
              [statuses, labels, members].forEach((q) => {
                if (q.isError) void q.refetch()
              })
            }
          >
            Retry filter options
          </button>
        </div>
      )}
      <QueryState
        pending={items.isPending}
        hasData={!!items.data}
        error={items.error}
        retry={items.refetch}
      >
        {!items.data?.items.length && (
          <Empty>No work items match these filters.</Empty>
        )}
        <ul className="workspace-rows work-item-list">
          {items.data?.items.map((item) => (
            <li key={item.id}>
              <WorkLink
                className="work-item-main work-item-card"
                href={workPath(workspace.id, project.id, item.id)}
                navigate={navigate}
              >
                <div className="work-item-link">
                  <span className="workspace-key">{item.key}</span>
                  <strong>{item.title}</strong>
                </div>
                <ItemBadges item={item} />
                <p className="muted work-item-meta">
                  {members.data?.find((m) => m.userId === item.assigneeUserId)
                    ?.displayName ||
                    (item.assigneeUserId
                      ? 'Previous or unavailable member'
                      : 'Unassigned')}
                  {item.dueDate ? ` · Due ${item.dueDate}` : ''}
                </p>
                {!!item.labelIds.length && (
                  <div className="workspace-actions">
                    {item.labelIds.map((id) => (
                      <span className="work-label" key={id}>
                        {labels.data?.find((l) => l.id === id)?.name ||
                          'Label unavailable'}
                      </span>
                    ))}
                  </div>
                )}
              </WorkLink>
            </li>
          ))}
        </ul>
        {items.data && (
          <Pager
            page={filters.page || 1}
            total={items.data.total}
            change={(page) => change({ page })}
          />
        )}
      </QueryState>
    </section>
  )
}
