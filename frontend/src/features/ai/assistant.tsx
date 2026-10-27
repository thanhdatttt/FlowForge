import { useEffect, useRef, useState, type FormEvent } from 'react'
import { useQueryClient } from '@tanstack/react-query'
import { Button } from '@/components/ui/button'
import { FormField } from '@/components/ui/form-field'
import { ApiError } from '@/lib/api'
import type { Project, Workspace } from '@/features/workspaces/api'
import { SelectField } from '@/features/workspaces/shared'
import { TextArea, WorkLink } from '@/features/work-items/shared'
import type { Scope } from '@/features/work-items/api'
import { invalidateKeys } from '@/features/work-items/queries'
import {
  canWriteWork,
  priorities,
  timestamp,
  workPath,
} from '@/features/work-items/rules'
import { Metrics } from '@/features/dashboard/dashboard'
import {
  aiApi,
  type Acceptance,
  type AcceptedTasks,
  type Preview,
  type ProjectSummary,
  type Proposal,
} from './api'
import { aiFailure, validateProposals } from './rules'
import { useAiRequest } from './use-ai-request'

export function AiAssistant({
  workspace,
  project,
  navigate,
}: {
  workspace: Workspace
  project: Project
  navigate: (path: string) => void
}) {
  const scope = { workspaceId: workspace.id, projectId: project.id }
  return (
    <div className="workspace-stack">
      <section className="workspace-card">
        <h2>AI Assistant</h2>
        <p className="muted">
          Draft tasks and interpret project progress. Review AI suggestions
          before using them. AI availability is managed by your server
          administrator.
        </p>
      </section>
      <TaskAssistant
        scope={scope}
        writable={canWriteWork(workspace.role, !!project.archivedAt)}
        navigate={navigate}
      />
      <SummaryAssistant scope={scope} />
    </div>
  )
}

function Failure({
  error,
  operation = 'generate',
}: {
  error: unknown
  operation?: 'generate' | 'accept'
}) {
  return error ? (
    <div className="error-notice" role="alert">
      <p>{aiFailure(error, operation)}</p>
      {error instanceof ApiError && Object.keys(error.fields).length > 0 && (
        <ul>
          {Object.entries(error.fields).map(([field, messages]) => (
            <li key={field}>
              {field}: {messages.join(' ')}
            </li>
          ))}
        </ul>
      )}
    </div>
  ) : null
}

function TaskAssistant({
  scope,
  writable,
  navigate,
}: {
  scope: Scope
  writable: boolean
  navigate: (path: string) => void
}) {
  const client = useQueryClient()
  const generation = useAiRequest()
  const acceptance = useAiRequest()
  const [description, setDescription] = useState('')
  const [preview, setPreview] = useState<Preview | null>(null)
  const [tasks, setTasks] = useState<Proposal[]>([])
  const [selected, setSelected] = useState<string[]>([])
  const [confirmed, setConfirmed] = useState(false)
  const [created, setCreated] = useState<AcceptedTasks | null>(null)
  // After an ambiguous network/server failure, retain exactly the attempted payload.
  // Editing it could conflict with an acceptance that already committed.
  const [attempt, setAttempt] = useState<Acceptance | null>(null)
  const [now, setNow] = useState(Date.now)
  const form = useRef<HTMLFormElement>(null)
  useEffect(() => {
    if (!preview) return
    const timer = setTimeout(
      () => setNow(Date.now()),
      Math.max(0, Date.parse(preview.expiresAt) - Date.now()),
    )
    return () => clearTimeout(timer)
  }, [preview])
  const chosen = tasks.filter((t) => selected.includes(t.id))
  const errors = validateProposals(chosen)
  const expired = !!preview && Date.parse(preview.expiresAt) <= now
  const denied = [generation.error, acceptance.error].some(
    (e) => e instanceof ApiError && [403, 404].includes(e.status),
  )
  const terminal =
    acceptance.error instanceof ApiError &&
    [409, 410].includes(acceptance.error.status)
  const busy = generation.busy || acceptance.busy
  const canEdit =
    writable &&
    !busy &&
    !attempt &&
    !created &&
    !expired &&
    !denied &&
    !terminal
  const clear = () => {
    generation.cancel()
    setPreview(null)
    setTasks([])
    setSelected([])
    setConfirmed(false)
    setCreated(null)
    setAttempt(null)
    generation.setError(null)
    acceptance.setError(null)
  }
  const update = (id: string, patch: Partial<Proposal>) => {
    setTasks((current) =>
      current.map((task) => (task.id === id ? { ...task, ...patch } : task)),
    )
    setConfirmed(false)
    acceptance.setError(null)
  }
  async function propose(event: FormEvent) {
    event.preventDefault()
    if (!writable || busy || preview || !description.trim()) return
    await generation.run(
      (signal) => aiApi.propose(scope, description, signal),
      (result) => {
        setPreview(result)
        setTasks(result.tasks)
        setSelected(result.tasks.map((t) => t.id))
        setNow(Date.now())
        setCreated(null)
        requestAnimationFrame(() =>
          document.getElementById('ai-proposal-heading')?.focus(),
        )
      },
    )
  }
  async function accept(event: FormEvent) {
    event.preventDefault()
    if (
      !writable ||
      busy ||
      !preview ||
      expired ||
      denied ||
      terminal ||
      created
    )
      return
    if (!attempt && (!confirmed || Object.keys(errors).length)) {
      form.current?.querySelector<HTMLElement>('[aria-invalid="true"]')?.focus()
      return
    }
    const payload: Acceptance = attempt || {
      previewToken: preview.previewToken,
      accepted: true,
      tasks: structuredClone(chosen),
    }
    setAttempt(payload)
    await acceptance.run(
      (signal) => aiApi.accept(scope, payload, signal),
      (result) => {
        setCreated(result)
        setAttempt(null)
        setConfirmed(false)
        for (const queryKey of invalidateKeys(scope))
          void client.invalidateQueries({ queryKey })
      },
      (failure) => {
        if (failure instanceof ApiError && failure.status === 400)
          setAttempt(null)
      },
    )
  }
  useEffect(() => {
    if (denied)
      void client.invalidateQueries({
        queryKey: ['phase2', scope.workspaceId],
      })
  }, [denied, client, scope.workspaceId])
  return (
    <section className="workspace-card">
      <h2>Task breakdown</h2>
      <p className="muted">
        Describe a feature, review the proposals, then explicitly confirm the
        tasks to create.
      </p>
      {!writable && (
        <p className="workspace-readonly" role="status">
          Task generation and creation require write access to an active
          project. Project summaries remain available below.
        </p>
      )}
      <form
        className="workspace-form ai-form"
        onSubmit={propose}
        aria-busy={generation.busy}
      >
        <TextArea
          label="Feature description"
          required
          maxLength={10_000}
          value={description}
          disabled={!writable || busy || !!preview || denied}
          onChange={(e) => setDescription(e.target.value)}
          hint="1–10,000 characters. Include the scope and expected result."
          error={
            generation.error instanceof ApiError
              ? generation.error.fields.featureDescription?.join(' ')
              : undefined
          }
        />
        <div className="workspace-actions">
          <Button
            type="submit"
            loading={generation.busy}
            disabled={
              !writable ||
              busy ||
              !!preview ||
              denied ||
              !description.trim() ||
              generation.cooldown > 0
            }
          >
            {generation.cooldown
              ? `Try again in ${generation.cooldown}s`
              : 'Generate task proposals'}
          </Button>
          {generation.busy && (
            <Button
              type="button"
              variant="secondary"
              onClick={generation.cancel}
            >
              Cancel generation
            </Button>
          )}
        </div>
      </form>
      <Failure error={generation.error} />
      {preview && !denied && (
        <div className="ai-preview">
          <h3 id="ai-proposal-heading" tabIndex={-1}>
            Review task proposals
          </h3>
          <p className="muted">
            AI-generated suggestions · Expires{' '}
            <time dateTime={preview.expiresAt}>
              {timestamp(preview.expiresAt)}
            </time>
            . Proposals are kept only while this screen is open.
          </p>
          <p className="muted">
            Selected tasks are created in Todo, unassigned, with you as
            reporter. Existing workflows may react to their creation.
          </p>
          {expired && !created && (
            <p className="error-notice" role="alert">
              {attempt
                ? 'This preview has expired. Check Work items for any previously confirmed tasks before starting another breakdown.'
                : 'This preview has expired. Discard it and generate new proposals.'}
            </p>
          )}
          {created ? (
            <div role="status" className="ai-created">
              <h3>
                {created.tasks.length}{' '}
                {created.tasks.length === 1 ? 'task' : 'tasks'} created
              </h3>
              <ul>
                {created.tasks.map((task) => (
                  <li key={task.id}>
                    <WorkLink
                      className="text-link"
                      href={workPath(
                        scope.workspaceId,
                        scope.projectId,
                        task.id,
                      )}
                      navigate={navigate}
                    >
                      {task.key} —{' '}
                      {tasks.find((t) => t.id === task.proposalId)?.title ||
                        task.proposalId}
                    </WorkLink>
                  </li>
                ))}
              </ul>
              <p>
                Your confirmation was saved. Repeated confirmation of the same
                selection does not create duplicates.
              </p>
            </div>
          ) : (
            <form
              ref={form}
              onSubmit={accept}
              className="workspace-form ai-form"
              aria-busy={acceptance.busy}
            >
              <ProposalEditor
                tasks={tasks}
                selected={selected}
                disabled={!canEdit}
                errors={{
                  ...errors,
                  ...(acceptance.error instanceof ApiError
                    ? Object.fromEntries(
                        Object.entries(acceptance.error.fields).map(
                          ([field, messages]) => [field, messages.join(' ')],
                        ),
                      )
                    : {}),
                }}
                update={update}
                select={(id, included) => {
                  setSelected((current) =>
                    included
                      ? [...current, id]
                      : current.filter((value) => value !== id),
                  )
                  setConfirmed(false)
                  acceptance.setError(null)
                }}
              />
              {Object.keys(errors).length > 0 && (
                <div className="error-notice" role="alert">
                  <ul>
                    {Object.entries(errors).map(([field, message]) => (
                      <li key={field}>{message}</li>
                    ))}
                  </ul>
                </div>
              )}
              <Failure error={acceptance.error} operation="accept" />
              {attempt && !acceptance.busy && !terminal && (
                <p className="workspace-readonly" role="status">
                  The confirmation may already have been saved. Retry the same
                  confirmation to retrieve its result without creating duplicate
                  tasks. The selection is locked until this is resolved.
                </p>
              )}
              <label className="ai-check">
                <input
                  type="checkbox"
                  checked={confirmed}
                  disabled={!canEdit}
                  onChange={(e) => setConfirmed(e.target.checked)}
                />{' '}
                I have reviewed these suggestions and want to create the
                selected tasks.
              </label>
              <Button
                type="submit"
                loading={acceptance.busy}
                disabled={
                  !writable ||
                  busy ||
                  expired ||
                  denied ||
                  terminal ||
                  acceptance.cooldown > 0 ||
                  (!attempt && (!confirmed || Object.keys(errors).length > 0))
                }
              >
                {acceptance.cooldown
                  ? `Try again in ${acceptance.cooldown}s`
                  : attempt
                    ? 'Retry same confirmation'
                    : `Create ${chosen.length} selected ${chosen.length === 1 ? 'task' : 'tasks'}`}
              </Button>
            </form>
          )}
          <Button
            variant="secondary"
            disabled={acceptance.busy || (!!attempt && !terminal && !expired)}
            onClick={clear}
          >
            {created ? 'Start another breakdown' : 'Discard preview'}
          </Button>
        </div>
      )}
    </section>
  )
}

export function ProposalEditor({
  tasks,
  selected,
  errors,
  disabled,
  update,
  select,
}: {
  tasks: Proposal[]
  selected: string[]
  errors: Record<string, string>
  disabled: boolean
  update: (id: string, patch: Partial<Proposal>) => void
  select: (id: string, included: boolean) => void
}) {
  const chosen = tasks.filter((task) => selected.includes(task.id))
  return (
    <div className="ai-proposal-list">
      {tasks.map((task, index) => {
        const included = selected.includes(task.id)
        const field = `tasks[${chosen.findIndex((t) => t.id === task.id)}]`
        const dependencyError = included
          ? errors[`${field}.dependsOn`]
          : undefined
        return (
          <fieldset className="ai-proposal" key={task.id} disabled={disabled}>
            <legend>
              Proposal {index + 1} · {task.id}
            </legend>
            <label className="ai-check">
              <input
                type="checkbox"
                checked={included}
                onChange={(e) => select(task.id, e.target.checked)}
              />{' '}
              Include proposal {index + 1}
            </label>
            <fieldset disabled={!included} className="ai-proposal-fields">
              <FormField
                label={`Task ${index + 1} title`}
                value={task.title}
                required
                maxLength={200}
                error={included ? errors[`${field}.title`] : undefined}
                onChange={(e) => update(task.id, { title: e.target.value })}
              />
              <TextArea
                label={`Task ${index + 1} description`}
                value={task.description || ''}
                maxLength={20_000}
                error={included ? errors[`${field}.description`] : undefined}
                onChange={(e) =>
                  update(task.id, { description: e.target.value || null })
                }
              />
              <SelectField
                label={`Task ${index + 1} priority`}
                value={task.priority}
                onChange={(e) =>
                  update(task.id, {
                    priority: e.target.value as Proposal['priority'],
                  })
                }
              >
                {priorities.map((priority) => (
                  <option key={priority}>{priority}</option>
                ))}
              </SelectField>
              <fieldset
                className="ai-dependencies"
                aria-describedby={
                  dependencyError ? `dependency-${task.id}-error` : undefined
                }
                aria-invalid={!!dependencyError}
                tabIndex={dependencyError ? -1 : undefined}
              >
                <legend>Task {index + 1} depends on</legend>
                {tasks.length === 1 && (
                  <p className="muted">No other proposals.</p>
                )}
                {tasks
                  .filter((other) => other.id !== task.id)
                  .map((other) => (
                    <label className="ai-check" key={other.id}>
                      <input
                        type="checkbox"
                        checked={task.dependsOn.includes(other.id)}
                        onChange={(e) =>
                          update(task.id, {
                            dependsOn: e.target.checked
                              ? [...task.dependsOn, other.id]
                              : task.dependsOn.filter((id) => id !== other.id),
                          })
                        }
                      />{' '}
                      {other.id} — {other.title || 'Untitled task'}
                      {!selected.includes(other.id) && ' (not selected)'}
                    </label>
                  ))}
                {dependencyError && (
                  <p id={`dependency-${task.id}-error`} className="field-error">
                    {dependencyError}
                  </p>
                )}
              </fieldset>
            </fieldset>
          </fieldset>
        )
      })}
    </div>
  )
}

function SummaryAssistant({ scope }: { scope: Scope }) {
  const client = useQueryClient()
  const request = useAiRequest()
  const [summary, setSummary] = useState<ProjectSummary | null>(null)
  const [stale, setStale] = useState(false)
  const revision = useRef(0)
  useEffect(
    () =>
      client.getQueryCache().subscribe((event) => {
        const key = event.query.queryKey
        if (
          event.type === 'updated' &&
          event.action.type === 'invalidate' &&
          ['phase2', 'phase3', 'phase6'].includes(String(key[0])) &&
          key[1] === scope.workspaceId &&
          (key[0] === 'phase2' || key[2] === scope.projectId)
        ) {
          revision.current++
          setStale(true)
        }
      }),
    [client, scope.workspaceId, scope.projectId],
  )
  const denied =
    request.error instanceof ApiError &&
    [403, 404].includes(request.error.status)
  const generate = () => {
    const started = revision.current
    void request.run(
      (signal) => aiApi.summary(scope, signal),
      (result) => {
        setSummary(result)
        setStale(started !== revision.current)
      },
    )
  }
  useEffect(() => {
    if (denied)
      void client.invalidateQueries({
        queryKey: ['phase2', scope.workspaceId],
      })
  }, [denied, client, scope.workspaceId])
  return (
    <section className="workspace-card">
      <div className="workspace-section-heading">
        <div>
          <h2>Project summary</h2>
          <p className="muted">
            Generate an AI interpretation from the project data you can access.
          </p>
        </div>
        <Button
          onClick={generate}
          loading={request.busy}
          disabled={request.cooldown > 0 || denied}
        >
          {request.cooldown
            ? `Try again in ${request.cooldown}s`
            : summary
              ? 'Generate again'
              : 'Generate summary'}
        </Button>
      </div>
      {request.busy && (
        <div className="workspace-actions">
          <p role="status">Generating summary…</p>
          <Button variant="secondary" onClick={request.cancel}>
            Cancel generation
          </Button>
        </div>
      )}
      <Failure error={request.error} />
      {summary && !denied && <SummaryView summary={summary} stale={stale} />}
    </section>
  )
}

export function SummaryView({
  summary,
  stale,
}: {
  summary: ProjectSummary
  stale: boolean
}) {
  return (
    <div className="ai-summary">
      {stale && (
        <p className="workspace-readonly" role="status">
          Project data may have changed. Generate again to update this summary.
        </p>
      )}
      <p className="muted">
        Snapshot <time dateTime={summary.asOf}>{timestamp(summary.asOf)}</time>{' '}
        · UTC date {summary.todayUtc}
      </p>
      <h3>Facts from project data</h3>
      <Metrics facts={summary.facts} />
      <p>{summary.facts.blockedItems} blocked items</p>
      {summary.status === 'InsufficientData' ? (
        <p role="status">
          There is not enough project data to generate a summary. Add work items
          and try again.
        </p>
      ) : (
        <>
          <h3>AI-generated interpretation</h3>
          <p className="muted">
            Review this interpretation against project facts. Context includes{' '}
            {summary.contextItemCount} work items.
          </p>
          {summary.contextTruncated && (
            <p className="workspace-readonly">
              AI received only a sample of work items. The factual metrics above
              cover all included project items.
            </p>
          )}
          {summary.narrative && (
            <>
              {(
                [
                  ['Overview', summary.narrative.overview],
                  ['Completed work', summary.narrative.completedWork],
                  ['Blockers', summary.narrative.blockers],
                  ['Overdue work', summary.narrative.overdueWork],
                ] as const
              ).map(([title, content]) => (
                <div key={title}>
                  <h4>{title}</h4>
                  <p className="ai-text">{content}</p>
                </div>
              ))}
              <h4>Potential risks · AI interpretation</h4>
              {summary.narrative.risks.length ? (
                <ul>
                  {summary.narrative.risks.map((risk, index) => (
                    <li className="ai-text" key={index}>
                      {risk}
                    </li>
                  ))}
                </ul>
              ) : (
                <p>No risks identified in the supplied context.</p>
              )}
            </>
          )}
        </>
      )}
    </div>
  )
}
