import { useState } from 'react'
import { Zap, ListChecks, ListOrdered } from 'lucide-react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { ApiError } from '@/lib/api'
import { Button } from '@/components/ui/button'
import {
  Empty,
  ErrorNotice,
  Modal,
  Pager,
  QueryState,
  SelectField,
  ActionForm,
} from '@/features/workspaces/shared'
import type { Workspace } from '@/features/workspaces/api'
import { WorkLink } from '@/features/work-items/shared'
import { workPath, timestamp } from '@/features/work-items/rules'
import { workflowApi, workflowKey, type Execution, type Workflow } from './api'
import { WorkflowEditor } from './editor'
import { useWorkflowProjects, workflowInvalidation } from './queries'
import {
  canBrowseWorkflows,
  conditionOutcome,
  executionPath,
  human,
  workflowPath,
} from './rules'

export function Workflows({
  workspace,
  userId,
  projectId,
  workflowId,
  executionId,
  navigate,
}: {
  workspace: Workspace
  userId: string
  projectId?: string
  workflowId?: string
  executionId?: string
  navigate: (path: string) => void
}) {
  if (!canBrowseWorkflows(workspace.role))
    return (
      <Empty>
        Only Owners, Admins and assigned project Managers can access workflows.
      </Empty>
    )
  if (executionId)
    return (
      <ExecutionPage
        key={executionId}
        workspaceId={workspace.id}
        workflowId={workflowId}
        executionId={executionId}
        navigate={navigate}
      />
    )
  if (workflowId)
    return (
      <WorkflowPage
        key={workflowId}
        workspace={workspace}
        userId={userId}
        id={workflowId}
        navigate={navigate}
      />
    )
  return (
    <WorkflowList
      key={projectId || 'workspace'}
      workspace={workspace}
      userId={userId}
      projectId={projectId}
      navigate={navigate}
    />
  )
}
function WorkflowList({
  workspace,
  userId,
  projectId,
  navigate,
}: {
  workspace: Workspace
  userId: string
  projectId?: string
  navigate: (path: string) => void
}) {
  const [page, setPage] = useState(1)
  const [filter, setFilter] = useState(projectId || '')
  const projects = useWorkflowProjects(workspace.id)
  const list = useQuery({
    queryKey: [...workflowKey(workspace.id), 'list', filter, page],
    queryFn: ({ signal }) =>
      workflowApi.list(workspace.id, page, filter || undefined, signal),
    retry: false,
  })
  const last = Math.max(1, Math.ceil((list.data?.total || 0) / 20))
  if (list.data && page > last) setPage(last)
  return (
    <section className="workspace-card">
      <div className="workspace-section-heading">
        <div>
          <h2>Workflows</h2>
          <p className="muted">
            Automate project work with triggers, conditions and ordered actions.
          </p>
        </div>
        <Modal
          title="Create workflow"
          trigger="Create workflow"
          popupClassName="workflow-dialog"
          description="New workflows start disabled. Configure and validate the rule, then enable it when ready."
        >
          {(close) => (
            <WorkflowEditor
              workspace={workspace}
              userId={userId}
              projectId={filter || undefined}
              saved={(result) => {
                close()
                navigate(workflowPath(workspace.id, result.id))
              }}
            />
          )}
        </Modal>
      </div>
      <SelectField
        label="Project filter"
        value={filter}
        disabled={!!projectId}
        onChange={(e) => {
          setFilter(e.target.value)
          setPage(1)
        }}
      >
        <option value="">
          All manageable rules, including workspace rules
        </option>
        {filter && !projects.data?.some((p) => p.id === filter) && (
          <option value={filter}>{filter}</option>
        )}
        {projects.data?.map((p) => (
          <option key={p.id} value={p.id}>
            {p.name}
            {p.archivedAt ? ' · archived' : ''}
          </option>
        ))}
      </SelectField>
      <ErrorNotice error={projects.error} />
      <QueryState
        pending={list.isPending}
        hasData={!!list.data}
        error={list.error}
        retry={list.refetch}
      >
        {!list.data?.items.length && (
          <Empty>No workflows in this scope yet.</Empty>
        )}
        <ul className="workspace-rows">
          {list.data?.items.map((w) => (
            <li key={w.id}>
              <div>
                <WorkLink
                  className="text-link"
                  href={workflowPath(workspace.id, w.id)}
                  navigate={navigate}
                >
                  {w.name}
                </WorkLink>
                <p className="muted">
                  {w.projectId
                    ? projects.data?.find((p) => p.id === w.projectId)?.name ||
                      `Project ${w.projectId}`
                    : 'Entire workspace'}{' '}
                  · {human(w.definition.trigger)} · Version {w.version}
                </p>
              </div>
              <span
                className="workspace-badge"
                data-status={w.isEnabled ? 'active' : 'archived'}
              >
                {w.isEnabled ? 'Enabled' : 'Disabled'}
              </span>
            </li>
          ))}
        </ul>
        {list.data && (
          <Pager page={page} total={list.data.total} change={setPage} />
        )}
      </QueryState>
    </section>
  )
}
function WorkflowPage({
  workspace,
  userId,
  id,
  navigate,
}: {
  workspace: Workspace
  userId: string
  id: string
  navigate: (path: string) => void
}) {
  const client = useQueryClient()
  const workflow = useQuery({
    queryKey: [...workflowKey(workspace.id), 'detail', id],
    queryFn: ({ signal }) => workflowApi.get(workspace.id, id, signal),
    retry: false,
    refetchInterval: (query) =>
      query.state.error instanceof ApiError &&
      [401, 403, 404].includes(query.state.error.status)
        ? false
        : 30000,
  })
  const projects = useWorkflowProjects(workspace.id)
  const [editing, setEditing] = useState(false)
  const [editorSession, setEditorSession] = useState(0)
  const [page, setPage] = useState(1)
  const history = useQuery({
    queryKey: [...workflowKey(workspace.id), 'history', id, page],
    queryFn: ({ signal }) =>
      workflowApi.executions(workspace.id, id, page, signal),
    enabled: !!workflow.data && !workflow.error,
    retry: false,
    refetchInterval: (query) =>
      query.state.error instanceof ApiError &&
      [401, 403, 404].includes(query.state.error.status)
        ? false
        : 15000,
  })
  const w = workflow.data
  const archived = !!projects.data?.find((p) => p.id === w?.projectId)
    ?.archivedAt
  const last = Math.max(1, Math.ceil((history.data?.total || 0) / 20))
  if (history.data && page > last) setPage(last)
  return (
    <div className="workspace-stack">
      <WorkLink
        className="text-link"
        href={workflowPath(workspace.id)}
        navigate={navigate}
      >
        ← Workflows
      </WorkLink>
      <QueryState
        pending={workflow.isPending}
        hasData={!!w}
        error={workflow.error}
        retry={workflow.refetch}
      >
        {w && (
          <>
            <section className="workspace-card">
              <div className="workspace-section-heading">
                <div>
                  <h2>{w.name}</h2>
                  <p className="muted">
                    Version {w.version} ·{' '}
                    {w.projectId
                      ? projects.data?.find((p) => p.id === w.projectId)
                          ?.name || `Project ${w.projectId}`
                      : 'Entire workspace'}
                  </p>
                </div>
                <span
                  className="workspace-badge"
                  data-status={w.isEnabled ? 'active' : 'archived'}
                >
                  {w.isEnabled ? 'Enabled' : 'Disabled'}
                </span>
              </div>
              <p className="muted workflow-updated">
                Updated{' '}
                <time dateTime={w.updatedAt}>{timestamp(w.updatedAt)}</time>
              </p>
              {archived && (
                <p className="workspace-readonly">
                  This project is archived. You can inspect history and disable
                  its workflow.
                </p>
              )}
              <ErrorNotice error={projects.error} />
              <div className="workspace-actions workflow-controls">
                <Button
                  variant="secondary"
                  disabled={
                    !editing &&
                    (archived || projects.isPending || !!projects.error)
                  }
                  onClick={() => {
                    setEditing(!editing)
                    setEditorSession((s) => s + 1)
                  }}
                >
                  {editing ? 'Close editor (discard draft)' : 'Edit workflow'}
                </Button>
                <WorkflowState
                  workspaceId={workspace.id}
                  workflow={w}
                  blocked={
                    !w.isEnabled &&
                    (archived || projects.isPending || !!projects.error)
                  }
                />
              </div>
              {editing ? (
                <WorkflowEditor
                  key={editorSession}
                  workspace={workspace}
                  userId={userId}
                  current={w}
                  saved={(result) => {
                    client.setQueryData(
                      [...workflowKey(workspace.id), 'detail', id],
                      result,
                    )
                    setEditing(false)
                  }}
                />
              ) : (
                <DefinitionView definition={w.definition} />
              )}
            </section>
            <section className="workspace-card">
              <h2>Execution history</h2>
              <p className="muted">
                Results retain the configuration version scheduled for each
                event. Updates refresh automatically; REST is authoritative.
              </p>
              <QueryState
                pending={history.isPending}
                hasData={!!history.data}
                error={history.error}
                retry={history.refetch}
              >
                {!history.data?.items.length && (
                  <Empty>
                    No executions yet. Enable the workflow and generate a
                    matching event.
                  </Empty>
                )}
                <ol className="workspace-rows">
                  {history.data?.items.map((e) => (
                    <li key={e.id}>
                      <div>
                        <WorkLink
                          className="text-link"
                          href={executionPath(workspace.id, e.id)}
                          navigate={navigate}
                        >
                          Execution · {timestamp(e.createdAt)}
                        </WorkLink>
                        <p className="muted">
                          Version {e.version} ·{' '}
                          {conditionOutcome(e.conditionsMatched)} ·{' '}
                          {e.attemptCount} attempt(s)
                        </p>
                        {e.safeError && (
                          <p className="field-error">{e.safeError}</p>
                        )}
                      </div>
                      <Status value={e.status} />
                    </li>
                  ))}
                </ol>
                {history.data && (
                  <Pager
                    page={page}
                    total={history.data.total}
                    change={setPage}
                  />
                )}
              </QueryState>
            </section>
          </>
        )}
      </QueryState>
    </div>
  )
}
function WorkflowState({
  workspaceId,
  workflow: w,
  blocked,
}: {
  workspaceId: string
  workflow: Workflow
  blocked: boolean
}) {
  return (
    <Modal
      title={w.isEnabled ? 'Disable workflow' : 'Enable workflow'}
      trigger={w.isEnabled ? 'Disable workflow' : 'Enable workflow'}
      triggerClassName={w.isEnabled ? 'button--disable' : 'button--enable'}
      popupClassName="workflow-state-dialog"
      description={
        w.isEnabled
          ? 'Cancels unfinished actions. Committed changes remain, and already queued email may still be delivered.'
          : 'Starts a new eligibility window; earlier events are not replayed. Targets and permissions are revalidated by the server.'
      }
    >
      {(close) => (
        <WorkflowStateForm
          workspaceId={workspaceId}
          workflow={w}
          blocked={blocked}
          close={close}
        />
      )}
    </Modal>
  )
}
function WorkflowStateForm({
  workspaceId,
  workflow,
  blocked,
  close,
}: {
  workspaceId: string
  workflow: Workflow
  blocked: boolean
  close: () => void
}) {
  const [snapshot] = useState(workflow)
  const [conflict, setConflict] = useState(false)
  return (
    <ActionForm
      label={snapshot.isEnabled ? 'Disable workflow' : 'Enable workflow'}
      submitClassName={
        snapshot.isEnabled ? 'button--disable' : 'button--enable'
      }
      disabled={blocked || conflict}
      onConflict={() => setConflict(true)}
      invalidateKeys={workflowInvalidation(workspaceId)}
      done={close}
      submit={() =>
        workflowApi.state(
          workspaceId,
          snapshot.id,
          !snapshot.isEnabled,
          snapshot.version,
        )
      }
    >
      {() => (
        <>
          {blocked && (
            <p>
              This project is archived or its current state is unavailable.
              Enabling is blocked.
            </p>
          )}
          {conflict && (
            <p role="alert">
              The workflow changed. Close and reopen this dialog to review its
              latest version.
            </p>
          )}
        </>
      )}
    </ActionForm>
  )
}
function ExecutionPage({
  workspaceId,
  workflowId,
  executionId,
  navigate,
}: {
  workspaceId: string
  workflowId?: string
  executionId: string
  navigate: (path: string) => void
}) {
  const result = useQuery({
    queryKey: [
      ...workflowKey(workspaceId),
      'execution',
      workflowId || 'lookup',
      executionId,
    ],
    queryFn: ({ signal }) =>
      workflowId
        ? workflowApi.execution(workspaceId, workflowId, executionId, signal)
        : workflowApi.lookupExecution(workspaceId, executionId, signal),
    retry: false,
    refetchInterval: (query) =>
      query.state.error instanceof ApiError &&
      [401, 403, 404].includes(query.state.error.status)
        ? false
        : 5000,
  })
  return (
    <QueryState
      pending={result.isPending}
      hasData={!!result.data}
      error={result.error}
      retry={result.refetch}
    >
      {result.data && (
        <ExecutionDetail
          workspaceId={workspaceId}
          execution={result.data}
          navigate={navigate}
        />
      )}
    </QueryState>
  )
}
export function ExecutionDetail({
  workspaceId,
  execution: e,
  navigate,
}: {
  workspaceId: string
  execution: Execution
  navigate: (path: string) => void
}) {
  return (
    <div className="workspace-stack">
      <WorkLink
        className="text-link"
        href={workflowPath(workspaceId, e.workflowId)}
        navigate={navigate}
      >
        ← Workflow and history
      </WorkLink>
      <section className="workspace-card">
        <div className="workspace-section-heading">
          <div>
            <h2>Execution details</h2>
            <p className="muted workflow-id">{e.id}</p>
          </div>
          <Status value={e.status} />
        </div>
        <p>{conditionOutcome(e.conditionsMatched)}</p>
        <ErrorNotice error={e.safeError ? new Error(e.safeError) : null} />
        <WorkLink
          className="text-link"
          href={workPath(workspaceId, e.projectId, e.workItemId)}
          navigate={navigate}
        >
          Open related work item
        </WorkLink>
        <dl className="workspace-metadata workflow-metadata">
          {[
            ['Configuration version', String(e.version)],
            ['Created', timestamp(e.createdAt)],
            ['Started', e.startedAt ? timestamp(e.startedAt) : 'Not started'],
            [
              'Finished',
              e.finishedAt ? timestamp(e.finishedAt) : 'Not finished',
            ],
            ['Attempts', String(e.attemptCount)],
            ['Event ID', e.eventId],
            ['Actor', e.actorUserId || 'System / no actor'],
            ['Project ID', e.projectId],
            ['Work item ID', e.workItemId],
            ['Version ID', e.workflowVersionId],
            ['Correlation', e.correlationId],
            ['Origin execution', e.originExecutionId || 'None'],
            ['Depth', String(e.depth)],
          ].map(([label, value]) => (
            <div key={label}>
              <dt>{label}</dt>
              <dd>
                {label === 'Origin execution' && e.originExecutionId ? (
                  <WorkLink
                    className="text-link"
                    href={executionPath(workspaceId, e.originExecutionId)}
                    navigate={navigate}
                  >
                    {value}
                  </WorkLink>
                ) : (
                  value
                )}
              </dd>
            </div>
          ))}
        </dl>
        <h3>Action outcomes</h3>
        <ol className="workspace-rows workflow-outcomes">
          {e.actions.map((a, i) => (
            <li key={a.actionId}>
              <div>
                <strong>
                  {i + 1}.{' '}
                  {human(
                    e.definition.actions.find((d) => d.id === a.actionId)
                      ?.type || 'Action',
                  )}
                </strong>
                <p className="muted">
                  {a.attemptCount} attempt(s) ·{' '}
                  {a.finishedAt ? timestamp(a.finishedAt) : 'Not finished'}
                </p>
                <p className="muted workflow-id">Action ID: {a.actionId}</p>
                {a.safeError && <p className="field-error">{a.safeError}</p>}
              </div>
              <Status value={a.status} />
            </li>
          ))}
        </ol>
        <h3>Email deliveries</h3>
        <p className="muted">
          A successful Send email action means queued. Delivery status below
          indicates whether it was sent, cancelled or failed.
        </p>
        {!e.emailDeliveries.length && (
          <p className="muted">No email delivery jobs.</p>
        )}
        <ul className="workspace-rows">
          {e.emailDeliveries.map((d) => (
            <li key={d.id}>
              <div>
                <strong className="workflow-id">
                  Recipient: {d.recipientUserId || 'Unavailable'}
                </strong>
                <p className="muted">
                  {d.attemptCount} attempt(s) ·{' '}
                  {d.completedAt ? timestamp(d.completedAt) : 'Not completed'}
                </p>
                {d.safeError && <p className="field-error">{d.safeError}</p>}
              </div>
              <Status value={d.status} />
            </li>
          ))}
        </ul>
        <h3>Configuration that ran · version {e.version}</h3>
        <DefinitionView definition={e.definition} />
      </section>
    </div>
  )
}
export function DefinitionView({
  definition: d,
}: {
  definition: Workflow['definition']
}) {
  return (
    <div className="workflow-definition">
      <section className="workflow-definition-section">
        <div className="workflow-definition-heading">
          <Zap size={18} aria-hidden="true" />
          <h3>Trigger</h3>
        </div>
        <p className="workflow-trigger">{human(d.trigger)}</p>
      </section>
      <section className="workflow-definition-section">
        <div className="workflow-definition-heading">
          <ListChecks size={18} aria-hidden="true" />
          <h3>Conditions</h3>
          <span className="workspace-badge">ALL · {d.conditions.length}</span>
        </div>
        {!d.conditions.length ? (
          <p className="muted">Every eligible event matches.</p>
        ) : (
          <ol className="workflow-definition-list">
            {d.conditions.map((c, i) => (
              <li key={i}>
                <span className="workflow-step" aria-hidden="true">
                  {i + 1}
                </span>
                <div>
                  <strong>{human(c.field)}</strong>
                  <span className="workflow-condition-operator">
                    {human(c.operator)}
                  </span>
                  {c.values.length > 0 && (
                    <ul className="workflow-condition-values">
                      {c.values.map((value, n) => (
                        <li key={n}>{value}</li>
                      ))}
                    </ul>
                  )}
                </div>
              </li>
            ))}
          </ol>
        )}
      </section>
      <section className="workflow-definition-section">
        <div className="workflow-definition-heading">
          <ListOrdered size={18} aria-hidden="true" />
          <h3>Ordered actions</h3>
          <span className="workspace-badge">{d.actions.length}</span>
        </div>
        <ol className="workflow-definition-list">
          {d.actions.map((a, i) => (
            <li key={a.id}>
              <span className="workflow-step" aria-hidden="true">
                {i + 1}
              </span>
              <div>
                <strong>{human(a.type)}</strong>
                {a.userId && (
                  <p className="workflow-definition-value">
                    Recipient: {a.userId}
                  </p>
                )}
                {a.value && (
                  <p className="workflow-definition-value">{a.value}</p>
                )}
              </div>
            </li>
          ))}
        </ol>
      </section>
    </div>
  )
}
function Status({ value }: { value: string }) {
  return (
    <span className="workspace-badge workflow-status" data-status={value}>
      {value}
    </span>
  )
}
