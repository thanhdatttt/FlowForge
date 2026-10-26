// Opt-in real frontend clients -> Vite proxy -> disposable API/PostgreSQL.
import assert from 'node:assert/strict'
import { randomUUID } from 'node:crypto'
import { mkdirSync, writeFileSync } from 'node:fs'
import { authApi } from '../src/features/auth/api.ts'
import {
  useSessionStore,
  type AuthTokens,
} from '../src/features/auth/session-store.ts'
import { workspaceApi, projectApi } from '../src/features/workspaces/api.ts'
import { workApi, type WorkItemInput } from '../src/features/work-items/api.ts'
import { dashboardApi } from '../src/features/dashboard/api.ts'
import { aiApi } from '../src/features/ai/api.ts'
import { createLiveUpdates } from '../src/features/notifications/realtime.ts'
import { accessToken } from '../src/lib/api.ts'
import { HubConnectionBuilder, LogLevel } from '@microsoft/signalr'

const origin = process.env.FLOWFORGE_LIVE_ORIGIN
if (
  !origin ||
  process.env.FLOWFORGE_PHASE6_DISPOSABLE !== '1' ||
  !['127.0.0.1', 'localhost'].includes(new URL(origin).hostname)
)
  throw Error('Explicit localhost disposable Phase 6 origin is required.')
const nativeFetch = globalThis.fetch
let requests = 0
globalThis.fetch = (url, init) => {
  requests++
  return nativeFetch(new URL(String(url), origin), init)
}
const checks: { name: string; passed: boolean }[] = []
async function check(name: string, action: () => Promise<void>) {
  await action()
  checks.push({ name, passed: true })
  console.log(`PASS ${name}`)
}
const output = new URL('../test-results/phase6/', import.meta.url)
mkdirSync(output, { recursive: true })
const password = 'Phase6-disposable-password!2026'
const accounts: Record<
  string,
  { email: string; id: string; tokens: AuthTokens }
> = {}
const suffix = randomUUID().slice(0, 8)
const as = (name: string) =>
  useSessionStore.getState().setTokens(accounts[name].tokens)
let live: ReturnType<typeof createLiveUpdates> | undefined
try {
  for (const name of ['owner', 'developer', 'viewer', 'outsider']) {
    const email = `phase6-${name}-${suffix}@example.test`
    const tokens = await authApi.register({
      email,
      password,
      displayName: `Phase 6 ${name}`,
    })
    useSessionStore.getState().setTokens(tokens)
    accounts[name] = { email, tokens, id: (await authApi.me()).id }
  }
  as('owner')
  const workspace = await workspaceApi.create(`Phase 6 verification ${suffix}`)
  await workspaceApi.addMember(
    workspace.id,
    accounts.developer.email,
    'Developer',
  )
  await workspaceApi.addMember(workspace.id, accounts.viewer.email, 'Viewer')
  const project = await projectApi.create(workspace.id, {
    name: 'Phase 6 dashboard',
    projectKey: 'PSIX',
    description: 'Disposable frontend integration',
    leadUserId: null,
    startsOn: null,
    targetOn: null,
  })
  const scope = { workspaceId: workspace.id, projectId: project.id }
  await projectApi.addMember(workspace.id, project.id, accounts.developer.id)
  await projectApi.addMember(workspace.id, project.id, accounts.viewer.id)
  const base: WorkItemInput = {
    title: 'Foundation',
    description: null,
    type: 'Task',
    status: 'Todo',
    priority: 'High',
    assigneeUserId: null,
    parentId: null,
    startDate: null,
    dueDate: null,
    estimateMinutes: 60,
    labelIds: [],
  }
  await check(
    'empty dashboard and summary return zeros/InsufficientData without AI configuration',
    async () => {
      const dashboard = await dashboardApi.overview(scope)
      assert.equal(dashboard.totalItems, 0)
      assert.equal(dashboard.completionPercentage, 0)
      assert.equal(dashboard.countsByPriority.length, 4)
      assert.ok(dashboard.countsByPriority.every((p) => p.count === 0))
      assert.equal((await dashboardApi.workload(scope)).total, 0)
      assert.equal((await dashboardApi.overdue(scope)).total, 0)
      const summary = await aiApi.summary(scope)
      assert.equal(summary.status, 'InsufficientData')
      assert.equal(summary.narrative, null)
      assert.equal(summary.isAiGenerated, false)
    },
  )
  const events: string[] = []
  const connection = new HubConnectionBuilder()
    .withUrl(`${origin}/hubs/updates`, { accessTokenFactory: accessToken })
    .configureLogging(LogLevel.None)
    .build()
  let recovered = false
  live = createLiveUpdates(connection, {
    state: () => {},
    recover: async () => {
      recovered = true
    },
    notification: () => {},
    project: (event) => {
      events.push(event.type)
    },
  })
  live.setScope(scope)
  const until = async (predicate: () => boolean) => {
    const deadline = Date.now() + 12_000
    while (!predicate() && Date.now() < deadline)
      await new Promise((resolve) => setTimeout(resolve, 100))
    assert.ok(predicate(), 'Expected realtime event')
  }
  await until(() => recovered)
  const overdueDate = new Date(Date.now() - 2 * 86400_000)
    .toISOString()
    .slice(0, 10)
  const overdue = await workApi.create(scope, {
    ...base,
    title: 'Overdue delivery',
    dueDate: overdueDate,
    assigneeUserId: accounts.developer.id,
    estimateMinutes: 120,
  })
  await workApi.create(scope, {
    ...base,
    title: 'Unassigned follow-up',
    type: 'Issue',
  })
  await workApi.create(scope, {
    ...base,
    title: 'Completed foundation',
    status: 'Done',
    parentId: overdue.id,
    estimateMinutes: null,
  })
  await check(
    'real SignalR updates follow committed work-item creation',
    async () => {
      await until(() => events.length > 0)
    },
  )
  await check(
    'dashboard counts task/issue/subtask and uses category-based completion and UTC overdue',
    async () => {
      const dashboard = await dashboardApi.overview(scope)
      assert.equal(dashboard.totalItems, 3)
      assert.equal(dashboard.completedItems, 1)
      assert.equal(dashboard.completionPercentage, 33.33)
      assert.equal(dashboard.overdueItems, 1)
      assert.ok(dashboard.recentActivity.length > 0)
      const rows = await dashboardApi.overdue(scope)
      assert.equal(rows.total, 1)
      assert.equal(rows.items[0].id, overdue.id)
      const workload = await dashboardApi.workload(scope)
      assert.equal(workload.total, 2)
      assert.equal(
        workload.items.find(
          (row) => row.assigneeUserId === accounts.developer.id,
        )?.estimateMinutes,
        120,
      )
      assert.equal(
        workload.items.find((row) => row.assigneeUserId === null)
          ?.estimateMinutes,
        60,
      )
    },
  )
  await check(
    'unconfigured AI reports 503, input errors 400, and never creates work items',
    async () => {
      await assert.rejects(
        aiApi.propose(scope, 'Create a feature'),
        (e: any) => e.status === 503,
      )
      await assert.rejects(aiApi.summary(scope), (e: any) => e.status === 503)
      await assert.rejects(
        aiApi.propose(scope, ' '),
        (e: any) => e.status === 400,
      )
      await assert.rejects(
        aiApi.accept(scope, {
          previewToken: 'invalid',
          accepted: true,
          tasks: [],
        }),
        (e: any) => e.status === 400,
      )
      assert.equal((await dashboardApi.overview(scope)).totalItems, 3)
    },
  )
  await check(
    'pagination out of range remains empty with true totals',
    async () => {
      const rows = await dashboardApi.workload(scope, 999)
      assert.equal(rows.total, 2)
      assert.equal(rows.items.length, 0)
      await assert.rejects(
        dashboardApi.overdue(scope, 0),
        (e: any) => e.status === 400,
      )
    },
  )
  await check(
    'Viewer reads dashboard/summary but cannot propose or accept',
    async () => {
      as('viewer')
      assert.equal((await dashboardApi.overview(scope)).totalItems, 3)
      await assert.rejects(
        aiApi.propose(scope, 'Feature'),
        (e: any) => e.status === 403,
      )
      await assert.rejects(
        aiApi.accept(scope, {
          previewToken: 'invalid',
          accepted: true,
          tasks: [],
        }),
        (e: any) => e.status === 403,
      )
      await assert.rejects(aiApi.summary(scope), (e: any) => e.status === 503)
    },
  )
  await check('outsider cannot read dashboard or summary', async () => {
    as('outsider')
    for (const operation of [
      () => dashboardApi.overview(scope),
      () => dashboardApi.overdue(scope),
      () => dashboardApi.workload(scope),
      () => aiApi.summary(scope),
    ])
      await assert.rejects(operation, (e: any) => e.status === 404)
  })
  await check(
    'archived project remains readable and rejects AI writes',
    async () => {
      as('owner')
      await projectApi.archive(workspace.id, project.id, true)
      assert.equal((await dashboardApi.overview(scope)).totalItems, 3)
      await assert.rejects(
        aiApi.propose(scope, 'Feature'),
        (e: any) => e.status === 409,
      )
      await projectApi.archive(workspace.id, project.id, false)
    },
  )
  writeFileSync(
    '/tmp/flowforge-phase6-browser.json',
    JSON.stringify(
      {
        origin,
        workspaceId: workspace.id,
        projectId: project.id,
        password,
        accounts: Object.fromEntries(
          Object.entries(accounts).map(([name, account]) => [
            name,
            { email: account.email },
          ]),
        ),
      },
      null,
      2,
    ),
    { mode: 0o600 },
  )
  writeFileSync(
    new URL('live-backend.json', output),
    JSON.stringify(
      {
        passed: true,
        scenarios: checks.length,
        requests,
        checks,
        realtimeEvents: events.length,
        realtimeEventTypes: [...new Set(events)],
        aiProvider: 'Unconfigured; no external call',
        backend: 'Disposable PostgreSQL/API via Vite proxy',
      },
      null,
      2,
    ),
  )
  console.log(`Passed ${checks.length} scenarios, ${requests} client requests.`)
} catch (error) {
  writeFileSync(
    new URL('live-backend.json', output),
    JSON.stringify(
      {
        passed: false,
        requests,
        checks,
        failure: error instanceof Error ? error.message : 'Unknown failure',
      },
      null,
      2,
    ),
  )
  throw error
} finally {
  await live?.stop()
  globalThis.fetch = nativeFetch
  useSessionStore.getState().clear()
}
