import { useQuery } from '@tanstack/react-query'
import type { Project, Workspace } from '@/features/workspaces/api'
import {
  Empty,
  Modal,
  QueryState,
  SelectField,
} from '@/features/workspaces/shared'
import { text } from '@/features/workspaces/rules'
import { workApi, type Scope, type WorkItem } from './api'
import { canWriteWork, timestamp, workPath } from './rules'
import { ItemBadges, WorkConfirm, WorkForm, WorkLink } from './shared'
import { useLabels, useMembers, useRelations, workKey } from './queries'
import { ItemEditor } from './editor'
import { Comments } from './comments'
import { Attachments } from './attachments'
import { TimeEntries } from './time-entries'
import { ActivityFeed } from './activity'

function Relations({
  scope,
  item,
  writable,
  navigate,
}: {
  scope: Scope
  item: WorkItem
  writable: boolean
  navigate: (path: string) => void
}) {
  const relations = useRelations(scope)
  const link = (id: string) => {
    const target = relations.data?.find((i) => i.id === id)
    return (
      <WorkLink
        className="text-link"
        href={workPath(scope.workspaceId, scope.projectId, id)}
        navigate={navigate}
      >
        {target
          ? `${target.key} · ${target.title}${target.archivedAt ? ' (archived)' : ''}`
          : 'Open related work item'}
      </WorkLink>
    )
  }
  const children = relations.data?.filter((i) => i.parentId === item.id) || []
  const dependencies =
    relations.data?.filter(
      (i) =>
        !i.archivedAt &&
        i.id !== item.id &&
        !item.dependsOnItemIds.includes(i.id),
    ) || []
  return (
    <section className="workspace-card" id="work-relations">
      <h2>Related work</h2>
      <QueryState
        pending={relations.isPending}
        hasData={!!relations.data}
        error={relations.error}
        retry={relations.refetch}
      >
        <h3>Parent</h3>
        <p>{item.parentId ? link(item.parentId) : 'No parent work item.'}</p>
        <div className="workspace-section-heading">
          <h3>Subtasks</h3>
          {writable && (
            <Modal title="New subtask" trigger="New subtask">
              {(close) => (
                <ItemEditor
                  scope={scope}
                  parentId={item.id}
                  saved={(created) => {
                    close()
                    navigate(
                      workPath(scope.workspaceId, scope.projectId, created.id),
                    )
                  }}
                />
              )}
            </Modal>
          )}
        </div>
        {!children.length && <p className="muted">No subtasks yet.</p>}
        <ul className="workspace-rows">
          {children.map((child) => (
            <li key={child.id}>
              <div>
                {link(child.id)}
                <ItemBadges item={child} />
              </div>
            </li>
          ))}
        </ul>
        <div className="workspace-section-heading">
          <h3>Depends on</h3>
          {writable && (
            <Modal
              title="Add dependency"
              trigger="Add dependency"
              description="Choose unfinished or completed work in this project. The server checks for dependency cycles."
            >
              {(close) => (
                <WorkForm
                  scope={scope}
                  label="Add dependency"
                  done={close}
                  submit={(data) =>
                    workApi.addDependency(
                      scope,
                      item.id,
                      text(data, 'dependsOnItemId'),
                    )
                  }
                >
                  {(error) => (
                    <SelectField
                      label="Work item"
                      name="dependsOnItemId"
                      required
                      defaultValue=""
                      error={error('dependsOnItemId')}
                    >
                      <option value="" disabled>
                        Select a work item
                      </option>
                      {dependencies.map((target) => (
                        <option key={target.id} value={target.id}>
                          {target.key} · {target.title}
                        </option>
                      ))}
                    </SelectField>
                  )}
                </WorkForm>
              )}
            </Modal>
          )}
        </div>
        {!item.dependsOnItemIds.length && (
          <p className="muted">No dependencies.</p>
        )}
        <ul className="workspace-rows">
          {item.dependsOnItemIds.map((id) => (
            <li key={id}>
              {link(id)}
              {writable && (
                <WorkConfirm
                  scope={scope}
                  title="Remove dependency?"
                  description="This removes the dependency link; both work items are preserved."
                  label={`Remove ${relations.data?.find((i) => i.id === id)?.key || 'dependency'}`}
                  run={() => workApi.removeDependency(scope, item.id, id)}
                />
              )}
            </li>
          ))}
        </ul>
      </QueryState>
    </section>
  )
}
export function WorkDetail({
  workspace,
  project,
  itemId,
  userId,
  navigate,
}: {
  workspace: Workspace
  project: Project
  itemId: string
  userId: string
  navigate: (path: string) => void
}) {
  const scope = { workspaceId: workspace.id, projectId: project.id }
  const result = useQuery({
    queryKey: [...workKey(scope), 'item', itemId],
    queryFn: ({ signal }) => workApi.get(scope, itemId, signal),
    retry: false,
  })
  const labels = useLabels(scope)
  const members = useMembers(scope)
  const item = result.data
  const writable = canWriteWork(
    workspace.role,
    !!project.archivedAt,
    !!item?.archivedAt,
  )
  const memberName = (id: string | null) =>
    id
      ? members.data?.find((m) => m.userId === id)?.displayName ||
        `Previous or unavailable member (${id})`
      : 'Unassigned'
  return (
    <QueryState
      pending={result.isPending}
      hasData={!!result.data}
      error={result.error}
      retry={result.refetch}
    >
      {item && (
        <div className="workspace-stack">
          <section className="workspace-card">
            <WorkLink
              className="text-link"
              href={workPath(workspace.id, project.id)}
              navigate={navigate}
            >
              ← All work items
            </WorkLink>
            <div className="workspace-section-heading work-detail-heading">
              <div>
                <span className="workspace-key">{item.key}</span>
                <h2>{item.title}</h2>
              </div>
              <div className="workspace-actions">
                {writable && (
                  <Modal title={`Edit ${item.key}`} trigger="Edit work item">
                    {(close) => (
                      <ItemEditor scope={scope} item={item} saved={close} />
                    )}
                  </Modal>
                )}
                {canWriteWork(workspace.role, !!project.archivedAt) && (
                  <WorkConfirm
                    scope={scope}
                    title={
                      item.archivedAt
                        ? 'Restore work item?'
                        : 'Archive work item?'
                    }
                    description={
                      item.archivedAt
                        ? 'Restore this item to allow changes again.'
                        : 'This item and its collaboration become read only until restored.'
                    }
                    label={
                      item.archivedAt
                        ? 'Restore work item'
                        : 'Archive work item'
                    }
                    run={() =>
                      workApi.archive(scope, item.id, !item.archivedAt)
                    }
                  />
                )}
              </div>
            </div>
            <ItemBadges item={item} />
            {item.needsReassignment && (
              <p className="workspace-readonly">
                The previous assignee no longer has access. Choose a current
                project member or unassign this item.
              </p>
            )}
            {item.archivedAt && (
              <p className="workspace-readonly">
                Archived work item · Read only. Restore it before changing its
                details or collaboration.
              </p>
            )}
            <p className="workspace-description work-description">
              {item.description || 'No description yet.'}
            </p>
            <dl className="workspace-metadata work-metadata">
              <div>
                <dt>Assignee</dt>
                <dd>{memberName(item.assigneeUserId)}</dd>
              </div>
              <div>
                <dt>Reporter</dt>
                <dd>{memberName(item.reporterUserId)}</dd>
              </div>
              <div>
                <dt>Start date</dt>
                <dd>{item.startDate || 'Not set'}</dd>
              </div>
              <div>
                <dt>Due date</dt>
                <dd>{item.dueDate || 'Not set'}</dd>
              </div>
              <div>
                <dt>Estimate</dt>
                <dd>
                  {item.estimateMinutes === null
                    ? 'Not set'
                    : `${item.estimateMinutes} minutes`}
                </dd>
              </div>
              <div>
                <dt>Created</dt>
                <dd>{timestamp(item.createdAt)}</dd>
              </div>
              <div>
                <dt>Updated</dt>
                <dd>{timestamp(item.updatedAt)}</dd>
              </div>
            </dl>
            <div className="workspace-actions work-labels" aria-label="Labels">
              {item.labelIds.map((id) => (
                <span key={id} className="work-label">
                  {labels.data?.find((l) => l.id === id)?.name ||
                    'Label unavailable'}
                </span>
              ))}
              {!item.labelIds.length && (
                <span className="muted">No labels</span>
              )}
            </div>
            {(members.isError || labels.isError) && (
              <p role="status" className="muted">
                Some member or label details are unavailable.{' '}
                <button
                  className="text-link"
                  onClick={() => {
                    void members.refetch()
                    void labels.refetch()
                  }}
                >
                  Retry details
                </button>
              </p>
            )}
            <nav className="work-section-links" aria-label="Work item sections">
              <a href="#work-relations">Related work</a>
              <a href="#work-comments">Comments</a>
              <a href="#work-files">Files</a>
              <a href="#work-time">Time</a>
              <a href="#work-activity">Activity</a>
            </nav>
          </section>
          {!writable && workspace.role === 'Viewer' && (
            <Empty>Your Viewer role has read-only access to this work.</Empty>
          )}
          <Relations
            scope={scope}
            item={item}
            writable={writable}
            navigate={navigate}
          />
          <Comments
            scope={scope}
            itemId={item.id}
            userId={userId}
            role={workspace.role}
            writable={writable}
          />
          <Attachments
            scope={scope}
            itemId={item.id}
            userId={userId}
            role={workspace.role}
            writable={writable}
          />
          <TimeEntries
            scope={scope}
            itemId={item.id}
            userId={userId}
            role={workspace.role}
            writable={writable}
          />
          <ActivityFeed scope={scope} itemId={item.id} />
        </div>
      )}
    </QueryState>
  )
}
