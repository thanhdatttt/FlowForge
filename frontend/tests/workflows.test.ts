import { afterEach, test } from 'node:test'
import assert from 'node:assert/strict'
import { createElement } from 'react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { WorkflowEditor } from '../src/features/workflows/editor.tsx'
import { renderToStaticMarkup } from 'react-dom/server'
import {
  workflowApi,
  type Definition,
  type Execution,
} from '../src/features/workflows/api.ts'
import {
  actionTypes,
  canBrowseWorkflows,
  canManageWorkflow,
  changeOperator,
  cleanDefinition,
  conditionOutcome,
  executionPath,
  move,
  newAction,
  operators,
  triggers,
  workflowPath,
} from '../src/features/workflows/rules.ts'
import { parseRoute } from '../src/features/workspaces/rules.ts'
import { useSessionStore } from '../src/features/auth/session-store.ts'
import {
  DefinitionView,
  ExecutionDetail,
} from '../src/features/workflows/workflows.tsx'

const original = globalThis.fetch
const w = '10000000-0000-0000-0000-000000000001'
const p = '20000000-0000-0000-0000-000000000001'
const id = '30000000-0000-0000-0000-000000000001'
const e = '40000000-0000-0000-0000-000000000001'
afterEach(() => {
  globalThis.fetch = original
  useSessionStore.getState().clear()
})
const definition: Definition = {
  trigger: 'WorkItemCreated',
  conditions: [],
  actions: [{ id, type: 'ChangePriority', value: 'Critical' }],
}
const signin = () =>
  useSessionStore.getState().setTokens({
    accessToken: 'access',
    refreshToken: 'refresh',
    accessTokenExpiresAt: new Date(Date.now() + 900000).toISOString(),
  })

test('all workflow operations retain auth, exact DTOs, versions and pagination', async () => {
  signin()
  const calls: { url: string; method: string; body?: unknown }[] = []
  globalThis.fetch = async (url, init) => {
    assert.equal(
      (init?.headers as Record<string, string> | undefined)?.Authorization,
      'Bearer access',
    )
    calls.push({
      url: String(url),
      method: init?.method || 'GET',
      body: init?.body ? JSON.parse(String(init.body)) : undefined,
    })
    return Response.json({ valid: true })
  }
  const input = { name: 'Critical work', projectId: p, definition }
  await workflowApi.list(w, 2, p)
  await workflowApi.create(w, input)
  await workflowApi.validate(w, input)
  await workflowApi.get(w, id)
  await workflowApi.update(w, id, { ...input, version: 7 })
  await workflowApi.state(w, id, true, 8)
  await workflowApi.state(w, id, false, 8)
  await workflowApi.executions(w, id, 3)
  await workflowApi.execution(w, id, e)
  await workflowApi.lookupExecution(w, e)
  const base = `/api/v1/workspaces/${w}/workflows`
  assert.deepEqual(calls, [
    {
      url: `${base}?page=2&pageSize=20&projectId=${p}`,
      method: 'GET',
      body: undefined,
    },
    { url: base, method: 'POST', body: input },
    { url: `${base}/validate`, method: 'POST', body: input },
    { url: `${base}/${id}`, method: 'GET', body: undefined },
    { url: `${base}/${id}`, method: 'PUT', body: { ...input, version: 7 } },
    { url: `${base}/${id}/enable`, method: 'POST', body: { version: 8 } },
    { url: `${base}/${id}/disable`, method: 'POST', body: { version: 8 } },
    {
      url: `${base}/${id}/executions?page=3&pageSize=20`,
      method: 'GET',
      body: undefined,
    },
    { url: `${base}/${id}/executions/${e}`, method: 'GET', body: undefined },
    { url: `${base}/executions/${e}`, method: 'GET', body: undefined },
  ])
})
test('workflow validation errors and conflicts preserve server fields without blindly retrying mutations', async () => {
  for (const status of [400, 403, 404, 409]) {
    signin()
    let calls = 0
    globalThis.fetch = async () => {
      calls++
      return Response.json(
        {
          title: 'Failed',
          errors: { 'definition.actions[1]': ['Target unavailable'] },
        },
        { status },
      )
    }
    await assert.rejects(
      workflowApi.update(w, id, {
        name: 'Draft',
        projectId: p,
        definition,
        version: 1,
      }),
      (error: any) =>
        error.status === status &&
        error.fields['definition.actions[1]'][0] === 'Target unavailable',
    )
    assert.equal(calls, 1)
    assert.ok(useSessionStore.getState().tokens)
  }
})
test('workspace workflow, project workflow and activity execution routes resolve validated UUIDs', () => {
  assert.deepEqual(parseRoute(workflowPath(w, id)), {
    workspaceId: w,
    section: 'workflows',
    workflowId: id,
    executionId: undefined,
  })
  assert.equal(parseRoute(executionPath(w, e)).executionId, e)
  assert.equal(
    parseRoute(`/workspaces/${w}/workflows/${id}/executions/${e}`).workflowId,
    id,
  )
  assert.equal(
    parseRoute(`/workspaces/${w}/projects/${p}/workflows`).projectSection,
    'workflows',
  )
  for (const path of [
    `/workspaces/${w}/workflows/invalid`,
    `/workspaces/${w}/workflow-executions/invalid`,
    `/workspaces/${w}/projects/${p}/workflows/${id}`,
  ])
    assert.equal(parseRoute(path).workspaceId, undefined)
})
test('workflow permissions distinguish workspace management from explicit project assignment', () => {
  for (const role of [
    'Owner',
    'Admin',
    'Manager',
    'Developer',
    'Viewer',
  ] as const) {
    assert.equal(
      canBrowseWorkflows(role),
      ['Owner', 'Admin', 'Manager'].includes(role),
    )
    assert.equal(
      canManageWorkflow(role, null, true),
      ['Owner', 'Admin'].includes(role),
    )
    assert.equal(
      canManageWorkflow(role, p, false),
      ['Owner', 'Admin'].includes(role),
    )
    assert.equal(
      canManageWorkflow(role, p, true),
      ['Owner', 'Admin', 'Manager'].includes(role),
    )
  }
})
test('operator transitions enforce empty/list/single arity without parsing text delimiters', () => {
  const c = {
    field: 'Title',
    operator: 'In',
    values: ['Comma, pipe | and\nnewline', 'Other'],
  }
  assert.deepEqual(changeOperator(c, 'Equals').values, [c.values[0]])
  assert.deepEqual(changeOperator(c, 'IsEmpty').values, [])
  assert.deepEqual(changeOperator({ ...c, values: [] }, 'In').values, [''])
  assert.ok(operators('DueDate').includes('LessThan'))
  assert.ok(operators('Priority').includes('GreaterThanOrEqual'))
  for (const field of [
    'Title',
    'Status',
    'Label',
    'Assignee',
    'Actor',
    'Type',
    'Description',
  ])
    assert.ok(!operators(field).includes('LessThan'))
})
test('ordered actions keep UUIDs; changing action type removes irrelevant parameters', () => {
  const first = newAction('Assign', id)
  const second = newAction('AddComment', e)
  assert.deepEqual(
    move([first, second], 1, -1).map((a) => a.id),
    [e, id],
  )
  assert.deepEqual(move([first, second], 0, -1), [first, second])
  assert.deepEqual(newAction('Unassign', first.id), { id, type: 'Unassign' })
  const clean = cleanDefinition({
    ...definition,
    actions: [
      { id, type: 'Unassign', userId: p, value: 'stale' },
      { id: e, type: 'Notify', userId: p, value: 'stale' },
    ],
  })
  assert.deepEqual(clean.actions, [
    { id, type: 'Unassign' },
    { id: e, type: 'Notify', userId: p },
  ])
  assert.equal(triggers.length, 7)
  assert.equal(actionTypes.length, 9)
})
test('execution views use pinned configuration, distinguish skips and email delivery from queue success, and escape content', () => {
  const execution: Execution = {
    id: e,
    workflowId: id,
    workflowVersionId: id,
    eventId: id,
    projectId: p,
    workItemId: id,
    status: 'Succeeded',
    conditionsMatched: false,
    attemptCount: 1,
    createdAt: '2026-10-23T00:00:00Z',
    startedAt: null,
    finishedAt: null,
    safeError: null,
    version: 2,
    definition: {
      ...definition,
      actions: [{ id, type: 'SendEmail', userId: p }],
    },
    correlationId: 'correlation',
    actorUserId: null,
    originExecutionId: null,
    depth: 0,
    actions: [
      {
        actionId: id,
        status: 'Cancelled',
        attemptCount: 0,
        finishedAt: null,
        safeError: 'Conditions did not match.',
      },
    ],
    emailDeliveries: [
      {
        id,
        recipientUserId: p,
        status: 'Pending',
        attemptCount: 0,
        completedAt: null,
        safeError: null,
      },
    ],
  }
  const html = renderToStaticMarkup(
    createElement(ExecutionDetail, {
      workspaceId: w,
      execution,
      navigate: () => {},
    }),
  )
  assert.match(html, /Not matched — actions skipped/)
  assert.match(html, /version 2/)
  assert.match(html, /successful Send email action means queued/)
  assert.match(html, /Pending/)
  assert.match(
    html,
    new RegExp(`/workspaces/${w}/projects/${p}/work-items/${id}`),
  )
  assert.equal(conditionOutcome(null), 'Not evaluated yet')
  const escaped = renderToStaticMarkup(
    createElement(DefinitionView, {
      definition: {
        ...definition,
        actions: [
          { id, type: 'AddComment', value: '<script>alert(1)</script>' },
        ],
      },
    }),
  )
  assert.doesNotMatch(escaped, /<script>/)
  assert.match(escaped, /&lt;script&gt;/)
})

function renderEditor(
  role: 'Owner' | 'Manager',
  assigned: boolean,
  archived = false,
) {
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  })
  client.setQueryData(
    ['phase2', w, 'workflow-projects'],
    [
      {
        id: p,
        name: 'Test project',
        archivedAt: archived ? '2026-10-23T00:00:00Z' : null,
      },
    ],
  )
  client.setQueryData(['phase2', w, 'workflow-targets', p], {
    members: assigned
      ? [{ userId: id, displayName: 'Assigned Manager', role: 'Manager' }]
      : [],
    statuses: [{ id, name: 'Todo' }],
    labels: [],
  })
  const html = renderToStaticMarkup(
    createElement(
      QueryClientProvider,
      { client },
      createElement(WorkflowEditor, {
        workspace: { id: w, name: 'Workspace', role },
        userId: id,
        projectId: p,
        current: {
          id,
          workspaceId: w,
          projectId: p,
          createdByUserId: id,
          name: 'Pinned draft',
          definition: {
            ...definition,
            actions: [{ id, type: 'AddComment', value: 'Draft comment' }],
          },
          isEnabled: false,
          version: 3,
          createdAt: '2026-10-23T00:00:00Z',
          updatedAt: '2026-10-23T00:00:00Z',
        },
        saved: () => {},
      }),
    ),
  )
  client.clear()
  return html
}
test('workflow editor has structured controls, mention picker, immutable scope and named order buttons', () => {
  const html = renderEditor('Owner', true)
  assert.match(html, /Workflow name/)
  assert.match(html, /Draft comment/)
  assert.match(html, /Mention a member/)
  assert.match(html, /Insert mention/)
  assert.match(html, /aria-label="Move action 1 up"/)
  assert.match(html, /<select[^>]*disabled=""[^>]*>[\s\S]*?Test project/)
  assert.doesNotMatch(html, /<button[^>]*disabled=""[^>]*>Save workflow/)
})
test('unassigned Manager editor blocks save while explicitly assigned Manager can save', () => {
  assert.match(
    renderEditor('Manager', false),
    /<button[^>]*disabled=""[^>]*>Save workflow/,
  )
  assert.doesNotMatch(
    renderEditor('Manager', true),
    /<button[^>]*disabled=""[^>]*>Save workflow/,
  )
})
test('archived project editor keeps draft visible inside disabled fields with an explanation', () => {
  const html = renderEditor('Owner', true, true)
  assert.match(html, /This project is archived/)
  assert.match(html, /<fieldset disabled="">/)
  assert.match(html, /Draft comment/)
})
