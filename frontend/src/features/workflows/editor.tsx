import { useRef, useState, type FormEvent } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { Button } from '@/components/ui/button'
import { FormField } from '@/components/ui/form-field'
import { ApiError } from '@/lib/api'
import {
  ErrorNotice,
  QueryState,
  SelectField,
} from '@/features/workspaces/shared'
import {
  workspaceApi,
  type Member,
  type Workspace,
} from '@/features/workspaces/api'
import { TextArea } from '@/features/work-items/shared'
import { workApi } from '@/features/work-items/api'
import {
  workflowApi,
  type Action,
  type Condition,
  type Definition,
  type Workflow,
} from './api'
import { useWorkflowProjects, workflowInvalidation } from './queries'
import {
  actionTypes,
  canManageWorkflow,
  changeOperator,
  cleanDefinition,
  emptyOperator,
  fields,
  human,
  move,
  newAction,
  operators,
  priorities,
  recipientAction,
  triggers,
  valueAction,
} from './rules'

export function WorkflowEditor({
  workspace,
  userId,
  current,
  projectId,
  saved,
}: {
  workspace: Workspace
  userId: string
  current?: Workflow
  projectId?: string
  saved: (workflow: Workflow) => void
}) {
  const client = useQueryClient()
  const projects = useWorkflowProjects(workspace.id)
  const [snapshot, setSnapshot] = useState(current)
  const [name, setName] = useState(current?.name || '')
  const [scope, setScope] = useState(current?.projectId || projectId || '')
  const [definition, setDefinition] = useState<Definition>(() =>
    current
      ? structuredClone(current.definition)
      : { trigger: triggers[0], conditions: [], actions: [newAction()] },
  )
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<unknown>(null)
  const [message, setMessage] = useState('')
  const [conflict, setConflict] = useState(false)
  const form = useRef<HTMLFormElement>(null)
  const selectedProject = projects.data?.find((p) => p.id === scope)
  const targets = useQuery({
    queryKey: ['phase2', workspace.id, 'workflow-targets', scope],
    queryFn: async ({ signal }) => {
      if (!scope)
        return {
          members: await workspaceApi.members(workspace.id, signal),
          statuses: [],
          labels: [],
        }
      const s = { workspaceId: workspace.id, projectId: scope }
      const [members, statuses, labels] = await Promise.all([
        workApi.members(s, signal),
        workApi.statuses(s, signal),
        workApi.labels(s, signal),
      ])
      return { members, statuses, labels }
    },
    retry: false,
  })
  // Workflow management for Managers requires explicit project membership. The
  // project-recipient API includes Managers only when explicitly assigned.
  const assigned = targets.data?.members.some((m) => m.userId === userId)
  const readonly = !!selectedProject?.archivedAt
  const stale =
    conflict || (!!snapshot && current?.version !== snapshot.version)
  const patch = (next: Definition) => {
    setDefinition(next)
    setError(null)
    setMessage('')
  }
  const conditions = definition.conditions
  const actions = definition.actions
  const fieldError = (field: string) =>
    error instanceof ApiError ? error.fields[field]?.join(' ') : undefined
  function reset() {
    if (!current) return
    setSnapshot(current)
    setName(current.name)
    setScope(current.projectId || '')
    setDefinition(structuredClone(current.definition))
    setConflict(false)
    setError(null)
    setMessage('Latest version loaded.')
  }
  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (
      busy ||
      stale ||
      readonly ||
      projects.error ||
      !targets.data ||
      targets.error ||
      !canManageWorkflow(workspace.role, scope || null, !!assigned)
    )
      return
    const validate =
      (event.nativeEvent as SubmitEvent).submitter?.getAttribute('value') ===
      'validate'
    const input = {
      name: name.trim(),
      projectId: scope || null,
      definition: cleanDefinition(definition),
      ...(snapshot ? { version: snapshot.version } : {}),
    }
    setBusy(true)
    setError(null)
    setMessage('')
    try {
      if (validate) {
        await workflowApi.validate(workspace.id, input)
        setMessage('Configuration is valid. It has not been saved or enabled.')
      } else {
        const result = snapshot
          ? await workflowApi.update(workspace.id, snapshot.id, input)
          : await workflowApi.create(workspace.id, input)
        await Promise.all(
          workflowInvalidation(workspace.id).map((queryKey) =>
            client.invalidateQueries({ queryKey }),
          ),
        )
        saved(result)
      }
    } catch (failure) {
      setError(failure)
      if (
        failure instanceof ApiError &&
        [403, 404, 409].includes(failure.status)
      ) {
        if (failure.status === 409) setConflict(true)
        await Promise.all(
          workflowInvalidation(workspace.id).map((queryKey) =>
            client.invalidateQueries({ queryKey }),
          ),
        )
      }
      requestAnimationFrame(() =>
        form.current
          ?.querySelector<HTMLElement>('[aria-invalid="true"]')
          ?.focus(),
      )
    } finally {
      setBusy(false)
    }
  }
  const changeCondition = (index: number, next: Condition) =>
    patch({
      ...definition,
      conditions: conditions.map((c, i) => (i === index ? next : c)),
    })
  const changeAction = (index: number, next: Action) =>
    patch({
      ...definition,
      actions: actions.map((a, i) => (i === index ? next : a)),
    })
  return (
    <QueryState
      pending={projects.isPending}
      hasData={!!projects.data}
      error={projects.error}
      retry={projects.refetch}
    >
      <form
        ref={form}
        onSubmit={submit}
        className="workspace-form workflow-editor"
        aria-busy={busy}
      >
        {stale && (
          <div className="workspace-readonly" role="alert">
            The workflow changed or became read only. Your draft is kept below;
            saving is blocked. Copy any changes you need before loading the
            latest version.
            <br />
            <Button type="button" variant="secondary" onClick={reset}>
              Discard draft and load latest version
            </Button>
          </div>
        )}
        {readonly && (
          <p role="status">
            This project is archived. Creating, editing and enabling its
            workflows is unavailable.
          </p>
        )}
        <fieldset disabled={busy || readonly}>
          <FormField
            label="Workflow name"
            required
            maxLength={100}
            pattern=".*\S.*"
            value={name}
            onChange={(e) => {
              setName(e.target.value)
              setMessage('')
            }}
            error={fieldError('name')}
          />
          <SelectField
            label="Scope"
            value={scope}
            disabled={!!snapshot}
            required={workspace.role === 'Manager'}
            error={fieldError('projectId')}
            onChange={(e) => {
              setScope(e.target.value)
              setError(null)
              setMessage('')
            }}
          >
            <option value="">
              {workspace.role === 'Manager'
                ? 'Choose an assigned project'
                : 'Entire workspace'}
            </option>
            {scope && !projects.data?.some((p) => p.id === scope) && (
              <option value={scope}>Unavailable project ({scope})</option>
            )}
            {projects.data
              ?.filter((p) => !p.archivedAt || p.id === scope)
              .map((p) => (
                <option key={p.id} value={p.id}>
                  {p.name}
                  {p.archivedAt ? ' · archived' : ''}
                </option>
              ))}
          </SelectField>
          <p className="muted">
            Scope and creator cannot change after creation. Actions use the
            creator’s current project write permissions.
          </p>
          <SelectField
            label="Trigger"
            value={definition.trigger}
            error={fieldError('definition.trigger')}
            onChange={(e) => patch({ ...definition, trigger: e.target.value })}
          >
            {triggers.map((t) => (
              <option key={t} value={t}>
                {human(t)}
              </option>
            ))}
          </SelectField>
          {definition.trigger.includes('DueDate') && (
            <p className="workspace-readonly">
              Calendar triggers use UTC today. Reached means due today; overdue
              means before today. Assigned and unassigned unfinished items are
              eligible.
            </p>
          )}
          <QueryState
            pending={targets.isPending}
            hasData={!!targets.data}
            error={targets.error}
            retry={targets.refetch}
          >
            <fieldset disabled={targets.isPending || !!targets.error}>
              <section aria-label="Conditions" className="workflow-section">
                <h3>Conditions · all must match</h3>
                <p className="muted">
                  Up to 20 conditions. With none, every eligible event matches.
                  Evaluated against current item state when execution starts.
                </p>
                <p
                  className="field-error"
                  role={
                    fieldError('definition.conditions') ? 'alert' : undefined
                  }
                >
                  {fieldError('definition.conditions')}
                </p>
                <ol className="workflow-builder-list">
                  {conditions.map((c, i) => (
                    <li key={i}>
                      <fieldset className="workflow-builder-row">
                        <legend>Condition {i + 1}</legend>
                        <div className="workspace-columns">
                          <SelectField
                            label="Field"
                            value={c.field}
                            onChange={(e) =>
                              changeCondition(i, {
                                field: e.target.value,
                                operator: 'Equals',
                                values: [''],
                              })
                            }
                          >
                            {fields.map((f) => (
                              <option key={f}>{f}</option>
                            ))}
                          </SelectField>
                          <SelectField
                            label="Operator"
                            value={c.operator}
                            onChange={(e) =>
                              changeCondition(
                                i,
                                changeOperator(c, e.target.value),
                              )
                            }
                          >
                            {operators(c.field).map((o) => (
                              <option key={o} value={o}>
                                {human(o)}
                              </option>
                            ))}
                          </SelectField>
                        </div>
                        {!emptyOperator(c.operator) &&
                          c.values.map((v, vi) => (
                            <div key={vi} className="workflow-value-row">
                              <TypedValue
                                label={`Value ${vi + 1}`}
                                field={c.field}
                                value={v}
                                members={targets.data?.members || []}
                                error={fieldError(
                                  `definition.conditions[${i}]`,
                                )}
                                change={(value) =>
                                  changeCondition(i, {
                                    ...c,
                                    values: c.values.map((old, n) =>
                                      n === vi ? value : old,
                                    ),
                                  })
                                }
                              />
                              {c.operator === 'In' && c.values.length > 1 && (
                                <Button
                                  type="button"
                                  variant="text"
                                  aria-label={`Remove value ${vi + 1} from condition ${i + 1}`}
                                  onClick={() =>
                                    changeCondition(i, {
                                      ...c,
                                      values: c.values.filter(
                                        (_, n) => n !== vi,
                                      ),
                                    })
                                  }
                                >
                                  Remove value
                                </Button>
                              )}
                            </div>
                          ))}
                        {c.operator === 'In' && (
                          <Button
                            type="button"
                            variant="secondary"
                            disabled={c.values.length >= 50}
                            onClick={() =>
                              changeCondition(i, {
                                ...c,
                                values: [...c.values, ''],
                              })
                            }
                          >
                            Add value
                          </Button>
                        )}
                        {emptyOperator(c.operator) &&
                          fieldError(`definition.conditions[${i}]`) && (
                            <p className="field-error" role="alert">
                              {fieldError(`definition.conditions[${i}]`)}
                            </p>
                          )}
                        <OrderButtons
                          label={`condition ${i + 1}`}
                          index={i}
                          count={conditions.length}
                          reorder={(d) =>
                            patch({
                              ...definition,
                              conditions: move(conditions, i, d),
                            })
                          }
                          remove={() =>
                            patch({
                              ...definition,
                              conditions: conditions.filter((_, n) => n !== i),
                            })
                          }
                        />
                      </fieldset>
                    </li>
                  ))}
                </ol>
                <Button
                  type="button"
                  variant="secondary"
                  disabled={conditions.length >= 20}
                  onClick={() =>
                    patch({
                      ...definition,
                      conditions: [
                        ...conditions,
                        {
                          field: 'Priority',
                          operator: 'Equals',
                          values: ['Medium'],
                        },
                      ],
                    })
                  }
                >
                  Add condition
                </Button>
              </section>
              <section aria-label="Actions" className="workflow-section">
                <h3>Actions · run in order</h3>
                <p className="muted">
                  Choose 1–20 actions. Earlier committed changes remain if a
                  later action fails.
                </p>
                {!scope && (
                  <p className="workspace-readonly">
                    Workspace actions resolve exact status and label names in
                    each project. A valid target in one project may be
                    unavailable in another; that execution will fail safely.
                  </p>
                )}
                {fieldError('definition.actions') && (
                  <p className="field-error" role="alert">
                    {fieldError('definition.actions')}
                  </p>
                )}
                <ol className="workflow-builder-list">
                  {actions.map((a, i) => (
                    <li key={a.id}>
                      <fieldset className="workflow-builder-row">
                        <legend>Action {i + 1}</legend>
                        <SelectField
                          label="Action type"
                          value={a.type}
                          error={fieldError(`definition.actions[${i}]`)}
                          onChange={(e) =>
                            changeAction(i, newAction(e.target.value, a.id))
                          }
                        >
                          {actionTypes.map((t) => (
                            <option key={t} value={t}>
                              {human(t)}
                            </option>
                          ))}
                        </SelectField>
                        {recipientAction(a.type) && (
                          <MemberValue
                            label="Recipient"
                            value={a.userId || ''}
                            members={targets.data?.members || []}
                            change={(userId) =>
                              changeAction(i, { ...a, userId })
                            }
                          />
                        )}
                        {valueAction(a.type) &&
                          (a.type === 'AddComment' ? (
                            <ActionComment
                              value={a.value || ''}
                              members={targets.data?.members || []}
                              change={(value) =>
                                changeAction(i, { ...a, value })
                              }
                              error={fieldError(`definition.actions[${i}]`)}
                            />
                          ) : a.type === 'ChangePriority' ? (
                            <TypedValue
                              label="Priority"
                              field="Priority"
                              value={a.value || ''}
                              members={[]}
                              change={(value) =>
                                changeAction(i, { ...a, value })
                              }
                            />
                          ) : scope ? (
                            <SelectField
                              label={
                                a.type === 'ChangeStatus'
                                  ? 'Active status'
                                  : 'Label'
                              }
                              required
                              value={a.value || ''}
                              onChange={(e) =>
                                changeAction(i, { ...a, value: e.target.value })
                              }
                            >
                              <option value="">Choose a target</option>
                              {a.value &&
                                !(
                                  a.type === 'ChangeStatus'
                                    ? targets.data?.statuses
                                    : targets.data?.labels
                                )?.some((x) => x.name === a.value) && (
                                  <option value={a.value}>
                                    {a.value} (unavailable)
                                  </option>
                                )}
                              {(a.type === 'ChangeStatus'
                                ? targets.data?.statuses
                                : targets.data?.labels
                              )?.map((x) => (
                                <option key={x.id} value={x.name}>
                                  {x.name}
                                </option>
                              ))}
                            </SelectField>
                          ) : (
                            <FormField
                              label={
                                a.type === 'ChangeStatus'
                                  ? 'Exact status name'
                                  : 'Exact label name'
                              }
                              required
                              maxLength={a.type === 'ChangeStatus' ? 40 : 50}
                              value={a.value || ''}
                              onChange={(e) =>
                                changeAction(i, { ...a, value: e.target.value })
                              }
                            />
                          ))}
                        {a.type === 'Notify' && (
                          <p className="muted">
                            Creates an in-app notification without automatically
                            sending email.
                          </p>
                        )}
                        {a.type === 'SendEmail' && (
                          <p className="muted">
                            Requires server SMTP configuration. Success queues a
                            generic email; delivery has its own status in
                            execution details.
                          </p>
                        )}
                        <OrderButtons
                          label={`action ${i + 1}`}
                          index={i}
                          count={actions.length}
                          reorder={(d) =>
                            patch({
                              ...definition,
                              actions: move(actions, i, d),
                            })
                          }
                          remove={() =>
                            patch({
                              ...definition,
                              actions: actions.filter((_, n) => n !== i),
                            })
                          }
                        />
                      </fieldset>
                    </li>
                  ))}
                </ol>
                <Button
                  type="button"
                  variant="secondary"
                  disabled={actions.length >= 20}
                  onClick={() =>
                    patch({ ...definition, actions: [...actions, newAction()] })
                  }
                >
                  Add action
                </Button>
              </section>
            </fieldset>
          </QueryState>
          <ErrorNotice error={error} />
          {error instanceof ApiError &&
            Object.keys(error.fields).length > 0 && (
              <ul className="error-notice error-summary" role="alert">
                {Object.entries(error.fields).map(([key, messages]) => (
                  <li key={key}>
                    {key}: {messages.join(' ')}
                  </li>
                ))}
              </ul>
            )}
          <p className="workflow-feedback success-notice" role="status">
            {message}
          </p>
          {workspace.role === 'Manager' && !scope && (
            <p className="muted">
              Choose a project where you are an assigned Manager.
            </p>
          )}
          <div className="workspace-actions">
            <Button
              type="submit"
              name="intent"
              value="validate"
              variant="secondary"
              disabled={
                stale ||
                !!targets.error ||
                targets.isPending ||
                !canManageWorkflow(workspace.role, scope || null, !!assigned)
              }
            >
              Validate configuration
            </Button>
            <Button
              type="submit"
              name="intent"
              value="save"
              disabled={
                stale ||
                !!targets.error ||
                targets.isPending ||
                !actions.length ||
                !canManageWorkflow(workspace.role, scope || null, !!assigned)
              }
            >
              {busy
                ? 'Working…'
                : snapshot
                  ? 'Save workflow'
                  : 'Create disabled workflow'}
            </Button>
          </div>
        </fieldset>
      </form>
    </QueryState>
  )
}
function OrderButtons({
  label,
  index,
  count,
  reorder,
  remove,
}: {
  label: string
  index: number
  count: number
  reorder: (direction: number) => void
  remove: () => void
}) {
  return (
    <div className="workspace-actions">
      <Button
        type="button"
        variant="text"
        aria-label={`Move ${label} up`}
        disabled={index === 0}
        onClick={() => reorder(-1)}
      >
        Move up
      </Button>
      <Button
        type="button"
        variant="text"
        aria-label={`Move ${label} down`}
        disabled={index === count - 1}
        onClick={() => reorder(1)}
      >
        Move down
      </Button>
      <Button
        type="button"
        variant="text"
        aria-label={`Remove ${label}`}
        onClick={remove}
      >
        Remove
      </Button>
    </div>
  )
}
function MemberValue({
  label,
  value,
  members,
  change,
  error,
}: {
  label: string
  value: string
  members: Member[]
  change: (value: string) => void
  error?: string
}) {
  return (
    <SelectField
      label={label}
      value={value}
      required
      error={error}
      onChange={(e) => change(e.target.value)}
    >
      <option value="">Choose a member</option>
      {value && !members.some((m) => m.userId === value) && (
        <option value={value}>Previous or unavailable member ({value})</option>
      )}
      {members.map((m) => (
        <option key={m.userId} value={m.userId}>
          {m.displayName} · {m.role}
        </option>
      ))}
    </SelectField>
  )
}
function TypedValue({
  label,
  field,
  value,
  members,
  change,
  error,
}: {
  label: string
  field: string
  value: string
  members: Member[]
  change: (value: string) => void
  error?: string
}) {
  if (field === 'Actor' || field === 'Assignee')
    return <MemberValue {...{ label, value, members, change, error }} />
  if (field === 'Priority' || field === 'Type')
    return (
      <SelectField
        label={label}
        value={value}
        required
        error={error}
        onChange={(e) => change(e.target.value)}
      >
        <option value="">Choose a value</option>
        {(field === 'Priority' ? priorities : ['Task', 'Issue']).map((v) => (
          <option key={v}>{v}</option>
        ))}
      </SelectField>
    )
  return (
    <FormField
      label={label}
      type={field === 'DueDate' ? 'date' : 'text'}
      required
      maxLength={200}
      value={value}
      error={error}
      onChange={(e) => change(e.target.value)}
      hint={
        ['Status', 'Label'].includes(field)
          ? 'Exact name, including case.'
          : undefined
      }
    />
  )
}

function ActionComment({
  value,
  members,
  change,
  error,
}: {
  value: string
  members: Member[]
  change: (value: string) => void
  error?: string
}) {
  const [mention, setMention] = useState('')
  return (
    <>
      <TextArea
        label="Comment"
        required
        maxLength={10000}
        value={value}
        error={error}
        onChange={(e) => change(e.target.value)}
        hint="Comments are plain text. Select a member below to add a mention."
      />
      <div className="work-mention-controls">
        <SelectField
          label="Mention a member"
          value={mention}
          onChange={(e) => setMention(e.target.value)}
        >
          <option value="">Choose a member</option>
          {members.map((m) => (
            <option key={m.userId} value={m.userId}>
              {m.displayName}
            </option>
          ))}
        </SelectField>
        <Button
          type="button"
          variant="secondary"
          disabled={
            !mention ||
            !members.some((m) => m.userId === mention) ||
            value.length + mention.length + 5 > 10000
          }
          onClick={() => {
            change(
              `${value}${value && !/\s$/.test(value) ? ' ' : ''}@{${mention}} `,
            )
            setMention('')
          }}
        >
          Insert mention
        </Button>
      </div>
    </>
  )
}
