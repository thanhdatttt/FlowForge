// Explicit localhost disposable API/PostgreSQL verification through real frontend clients.
import assert from 'node:assert/strict'
import { randomUUID } from 'node:crypto'
import { mkdirSync, writeFileSync } from 'node:fs'
import { HubConnectionBuilder, LogLevel } from '@microsoft/signalr'
import { authApi } from '../src/features/auth/api.ts'
import {
  useSessionStore,
  type AuthTokens,
} from '../src/features/auth/session-store.ts'
import { accessToken } from '../src/lib/api.ts'
import { workspaceApi, projectApi } from '../src/features/workspaces/api.ts'
import {
  workApi,
  type Scope,
  type WorkItemInput,
} from '../src/features/work-items/api.ts'
import {
  workflowApi,
  type Action,
  type Definition,
  type WorkflowInput,
} from '../src/features/workflows/api.ts'
import { notificationApi } from '../src/features/notifications/api.ts'
import { createLiveUpdates } from '../src/features/notifications/realtime.ts'

const origin = process.env.FLOWFORGE_LIVE_ORIGIN
if (
  !origin ||
  process.env.FLOWFORGE_PHASE5_DISPOSABLE !== '1' ||
  !['localhost', '127.0.0.1'].includes(new URL(origin).hostname)
)
  throw Error('Explicit localhost disposable phase5 origin is required.')
const nativeFetch = globalThis.fetch
let requests = 0
// The in-memory session is switched explicitly for authorization checks.
globalThis.fetch = (url, init) => {
  requests++
  return nativeFetch(new URL(String(url), origin), init)
}
const output = new URL('../test-results/phase5/', import.meta.url)
mkdirSync(output, { recursive: true })
const checks: { name: string; passed: boolean }[] = []
async function check(name: string, run: () => Promise<void>) {
  await run()
  checks.push({ name, passed: true })
  console.log(`PASS ${name}`)
}
const delay = (ms: number) => new Promise((resolve) => setTimeout(resolve, ms))
async function until<T>(
  load: () => Promise<T>,
  predicate: (value: T) => boolean,
  timeout = 20000,
): Promise<T> {
  const end = Date.now() + timeout
  while (Date.now() < end) {
    const value = await load()
    if (predicate(value)) return value
    await delay(200)
  }
  throw Error('Timed out waiting for workflow state')
}
const suffix = randomUUID().slice(0, 8)
const password = 'Phase5-test-password!2026'
const accounts: Record<
  string,
  { email: string; id: string; tokens: AuthTokens }
> = {}
const as = (name: string) =>
  useSessionStore.getState().setTokens(accounts[name].tokens)
const action = (type: string, value?: string, userId?: string): Action => ({
  id: randomUUID(),
  type,
  ...(value ? { value } : {}),
  ...(userId ? { userId } : {}),
})
let scope!: Scope
let mainId = ''
let itemId = ''
let executionId = ''
let controller: ReturnType<typeof createLiveUpdates> | undefined
const events: string[] = []
const itemInput: WorkItemInput = {
  title: 'Phase 5 live item',
  description: 'Disposable frontend verification',
  type: 'Task',
  status: 'Todo',
  priority: 'Medium',
  assigneeUserId: null,
  parentId: null,
  startDate: null,
  dueDate: null,
  estimateMinutes: null,
  labelIds: [],
}
let rule!: WorkflowInput
try {
  await check(
    'real disposable accounts, workspace, project and explicit role memberships',
    async () => {
      for (const name of [
        'owner',
        'manager',
        'developer',
        'viewer',
        'outsider',
      ]) {
        const email = `phase5-${name}-${suffix}@example.test`
        const tokens = await authApi.register({
          email,
          password,
          displayName: `Phase 5 ${name}`,
        })
        useSessionStore.getState().setTokens(tokens)
        accounts[name] = { email, tokens, id: (await authApi.me()).id }
      }
      as('owner')
      const w = await workspaceApi.create(`Phase 5 verification ${suffix}`)
      for (const [name, role] of [
        ['manager', 'Manager'],
        ['developer', 'Developer'],
        ['viewer', 'Viewer'],
      ] as const)
        await workspaceApi.addMember(w.id, accounts[name].email, role)
      const p = await projectApi.create(w.id, {
        name: 'Workflow verification',
        projectKey: 'PFIVE',
        description: null,
        leadUserId: null,
        startsOn: null,
        targetOn: null,
      })
      scope = { workspaceId: w.id, projectId: p.id }
      for (const name of ['manager', 'developer', 'viewer'])
        await projectApi.addMember(w.id, p.id, accounts[name].id)
      await workApi.saveLabel(scope, { name: 'automated', color: '#4056c6' })
    },
  )
  await check(
    'validate, create-disabled, read and list follow actual workflow contracts',
    async () => {
      const definition: Definition = {
        trigger: 'WorkItemCreated',
        conditions: [{ field: 'Type', operator: 'Equals', values: ['Task'] }],
        actions: [
          action('Assign', undefined, accounts.developer.id),
          action('ChangeStatus', 'InProgress'),
          action('ChangePriority', 'Critical'),
          action('AddLabel', 'automated'),
          action('RemoveLabel', 'automated'),
          action('AddComment', `Automatic review @{${accounts.developer.id}}`),
          action('Notify', undefined, accounts.developer.id),
          action('Unassign'),
        ],
      }
      rule = {
        name: 'Live phase five rule',
        projectId: scope.projectId,
        definition,
      }
      assert.deepEqual(await workflowApi.validate(scope.workspaceId, rule), {
        valid: true,
      })
      await assert.rejects(
        workflowApi.validate(scope.workspaceId, {
          ...rule,
          definition: {
            ...definition,
            conditions: [
              { field: 'Title', operator: 'GreaterThan', values: ['x'] },
            ],
          },
        }),
        (e: any) => e.status === 400 && !!e.fields['definition.conditions[0]'],
      )
      const created = await workflowApi.create(scope.workspaceId, rule)
      mainId = created.id
      assert.equal(created.isEnabled, false)
      assert.equal(created.version, 1)
      assert.equal(
        (await workflowApi.get(scope.workspaceId, mainId)).name,
        rule.name,
      )
      assert.equal(
        (await workflowApi.list(scope.workspaceId, 1, scope.projectId)).total,
        1,
      )
      await workApi.create(scope, { ...itemInput, title: 'Before enablement' })
      await delay(1200)
      assert.equal(
        (await workflowApi.executions(scope.workspaceId, mainId)).total,
        0,
      )
    },
  )
  await check(
    'real SignalR subscribes and workflow execution invalidation event is delivered',
    async () => {
      as('owner')
      let ready = false
      const connection = new HubConnectionBuilder()
        .withUrl(`${origin}/hubs/updates`, { accessTokenFactory: accessToken })
        .configureLogging(LogLevel.None)
        .build()
      controller = createLiveUpdates(connection, {
        state: (state) => {
          ready = state === 'connected'
        },
        recover: async () => {
          await workflowApi.list(scope.workspaceId)
        },
        project: (e) => {
          if (e.type === 'WorkflowExecutionUpdated') events.push(e.entityId)
        },
        notification: () => {},
      })
      controller.setScope(scope)
      await until(
        async () => ready,
        (v) => v,
      )
      assert.equal(
        (await workflowApi.state(scope.workspaceId, mainId, true, 1)).isEnabled,
        true,
      )
      const item = await workApi.create(scope, itemInput)
      itemId = item.id
      const history = await until(
        () => workflowApi.executions(scope.workspaceId, mainId),
        (h) => h.items.some((e) => e.status === 'Succeeded'),
      )
      executionId = history.items[0].id
      await until(
        async () => events.includes(executionId),
        (v) => v,
      )
      await controller.stop()
      controller = undefined
    },
  )
  await check(
    'ordered effects, pinned detail, lookup, activity link and workflow notification are persisted',
    async () => {
      const item = await workApi.get(scope, itemId)
      assert.equal(item.priority, 'Critical')
      assert.equal(item.status, 'InProgress')
      assert.equal(item.assigneeUserId, null)
      assert.deepEqual(item.labelIds, [])
      assert.equal((await workApi.comments(scope, itemId, 1)).total, 1)
      const detail = await workflowApi.execution(
        scope.workspaceId,
        mainId,
        executionId,
      )
      assert.equal(detail.version, 1)
      assert.equal(detail.conditionsMatched, true)
      assert.equal(detail.actions.length, 8)
      assert.ok(detail.actions.every((a) => a.status === 'Succeeded'))
      assert.deepEqual(
        await workflowApi.lookupExecution(scope.workspaceId, executionId),
        detail,
      )
      const activity = await workApi.activity(scope, 1, itemId)
      assert.ok(
        activity.items.some((a) => a.workflowExecutionId === executionId),
      )
      as('developer')
      await until(
        () => notificationApi.list(),
        (list) => list.items.some((n) => n.type === 'Workflow'),
      )
      as('owner')
    },
  )
  await check(
    'execution lookup denies developer, viewer, outsider, anonymous and wrong workspace',
    async () => {
      for (const name of ['developer', 'viewer', 'outsider']) {
        as(name)
        await assert.rejects(
          workflowApi.lookupExecution(scope.workspaceId, executionId),
          { status: 404 },
        )
      }
      useSessionStore.getState().clear()
      assert.equal(
        (
          await nativeFetch(
            `${origin}/api/v1/workspaces/${scope.workspaceId}/workflows/executions/${executionId}`,
          )
        ).status,
        401,
      )
      as('owner')
      await assert.rejects(
        workflowApi.lookupExecution(randomUUID(), executionId),
        { status: 404 },
      )
    },
  )
  await check(
    'assigned Manager manages project rules but cannot create or inspect workspace rules',
    async () => {
      as('manager')
      const managed = await workflowApi.create(scope.workspaceId, {
        ...rule,
        name: 'Manager project rule',
      })
      assert.equal(managed.projectId, scope.projectId)
      assert.equal(
        (await workflowApi.lookupExecution(scope.workspaceId, executionId)).id,
        executionId,
      )
      await assert.rejects(
        workflowApi.create(scope.workspaceId, { ...rule, projectId: null }),
        { status: 403 },
      )
      as('owner')
      const workspaceRule = await workflowApi.create(scope.workspaceId, {
        ...rule,
        projectId: null,
        name: 'Workspace rule',
      })
      as('manager')
      await assert.rejects(
        workflowApi.get(scope.workspaceId, workspaceRule.id),
        { status: 404 },
      )
      as('owner')
    },
  )
  await check(
    'editing creates immutable versions, rejects stale writes and preserves the definition that ran',
    async () => {
      const updated = await workflowApi.update(scope.workspaceId, mainId, {
        ...rule,
        name: 'Updated phase five rule',
        definition: {
          ...rule.definition,
          actions: [action('AddComment', 'Version two')],
        },
        version: 1,
      })
      assert.equal(updated.version, 2)
      assert.equal(updated.isEnabled, true)
      await assert.rejects(
        workflowApi.update(scope.workspaceId, mainId, { ...rule, version: 1 }),
        { status: 409 },
      )
      await assert.rejects(
        workflowApi.state(scope.workspaceId, mainId, false, 1),
        { status: 409 },
      )
      assert.equal(
        (await workflowApi.lookupExecution(scope.workspaceId, executionId))
          .definition.actions.length,
        8,
      )
      assert.equal(
        (await workflowApi.lookupExecution(scope.workspaceId, executionId))
          .version,
        1,
      )
      const item = await workApi.create(scope, {
        ...itemInput,
        title: 'Version two item',
      })
      const history = await until(
        () => workflowApi.executions(scope.workspaceId, mainId),
        (h) =>
          h.items.some(
            (e) => e.workItemId === item.id && e.status === 'Succeeded',
          ),
      )
      assert.equal(
        history.items.find((e) => e.workItemId === item.id)?.version,
        2,
      )
    },
  )
  await check(
    'nonmatching conditions expose skipped actions rather than successful side effects',
    async () => {
      const r = await workflowApi.create(scope.workspaceId, {
        ...rule,
        name: 'Only issues',
        definition: {
          ...rule.definition,
          conditions: [
            { field: 'Type', operator: 'Equals', values: ['Issue'] },
          ],
          actions: [action('Unassign')],
        },
      })
      await workflowApi.state(scope.workspaceId, r.id, true, r.version)
      const item = await workApi.create(scope, {
        ...itemInput,
        title: 'Task does not match',
      })
      const h = await until(
        () => workflowApi.executions(scope.workspaceId, r.id),
        (h) =>
          h.items.some(
            (e) => e.workItemId === item.id && e.status === 'Succeeded',
          ),
      )
      const e = h.items.find((e) => e.workItemId === item.id)!
      assert.equal(e.conditionsMatched, false)
      assert.ok(e.actions.every((a) => a.status === 'Cancelled'))
      await workflowApi.state(scope.workspaceId, r.id, false, 1)
    },
  )
  await check(
    'missing SMTP produces visible permanent action diagnostics without claiming sent email',
    async () => {
      const r = await workflowApi.create(scope.workspaceId, {
        ...rule,
        name: 'Email diagnostics',
        definition: {
          ...rule.definition,
          conditions: [
            { field: 'Title', operator: 'Equals', values: ['Email test'] },
          ],
          actions: [action('SendEmail', undefined, accounts.developer.id)],
        },
      })
      await workflowApi.state(scope.workspaceId, r.id, true, 1)
      const item = await workApi.create(scope, {
        ...itemInput,
        title: 'Email test',
      })
      const h = await until(
        () => workflowApi.executions(scope.workspaceId, r.id),
        (h) =>
          h.items.some(
            (e) => e.workItemId === item.id && e.status === 'Failed',
          ),
      )
      const e = h.items.find((e) => e.workItemId === item.id)!
      assert.ok(e.safeError)
      assert.equal(e.actions[0].status, 'Failed')
      assert.equal(e.emailDeliveries.length, 0)
      await workflowApi.state(scope.workspaceId, r.id, false, 1)
    },
  )
  await check(
    'disable stops future scheduling and preserves committed effects and history',
    async () => {
      await workflowApi.state(scope.workspaceId, mainId, false, 2)
      const before = (await workflowApi.executions(scope.workspaceId, mainId))
        .total
      await workApi.create(scope, { ...itemInput, title: 'After disablement' })
      await delay(1500)
      assert.equal(
        (await workflowApi.executions(scope.workspaceId, mainId)).total,
        before,
      )
      assert.equal((await workApi.get(scope, itemId)).priority, 'Critical')
      assert.equal(
        (await workflowApi.lookupExecution(scope.workspaceId, executionId))
          .status,
        'Succeeded',
      )
    },
  )
  // Test credentials are deliberately temporary and are never part of the committed report.
  writeFileSync(
    '/tmp/flowforge-phase5-browser.json',
    JSON.stringify({
      origin,
      password,
      accounts,
      scope,
      mainId,
      itemId,
      executionId,
    }),
    { mode: 0o600 },
  )
} catch (error) {
  checks.push({
    name: error instanceof Error ? error.message : 'Unexpected failure',
    passed: false,
  })
  process.exitCode = 1
} finally {
  await controller?.stop()
  writeFileSync(
    new URL('live-backend.json', output),
    JSON.stringify(
      {
        checkedAt: new Date().toISOString(),
        disposable: true,
        requests,
        checks,
        workflowEvents: events.length,
      },
      null,
      2,
    ) + '\n',
  )
  useSessionStore.getState().clear()
  globalThis.fetch = nativeFetch
}
