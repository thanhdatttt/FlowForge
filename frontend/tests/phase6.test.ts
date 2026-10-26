import { afterEach, test } from 'node:test'
import assert from 'node:assert/strict'
import { createElement } from 'react'
import { renderToStaticMarkup } from 'react-dom/server'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { useSessionStore } from '../src/features/auth/session-store.ts'
import { ApiError } from '../src/lib/api.ts'
import { parseRoute } from '../src/features/workspaces/rules.ts'
import { invalidateKeys } from '../src/features/work-items/queries.ts'
import {
  dashboardApi,
  dashboardKey,
  type Dashboard,
} from '../src/features/dashboard/api.ts'
import { ProjectDashboard } from '../src/features/dashboard/dashboard.tsx'
import {
  aiApi,
  type Proposal,
  type ProjectSummary,
} from '../src/features/ai/api.ts'
import { aiFailure, validateProposals } from '../src/features/ai/rules.ts'
import {
  AiAssistant,
  ProposalEditor,
  SummaryView,
} from '../src/features/ai/assistant.tsx'

const w = '10000000-0000-0000-0000-000000000001',
  p = '20000000-0000-0000-0000-000000000001'
const scope = { workspaceId: w, projectId: p }
const nativeFetch = globalThis.fetch
afterEach(() => {
  globalThis.fetch = nativeFetch
  useSessionStore.getState().clear()
})
function signin() {
  useSessionStore.getState().setTokens({
    accessToken: 'access',
    refreshToken: 'refresh',
    accessTokenExpiresAt: new Date(Date.now() + 900_000).toISOString(),
  })
}
const proposals: Proposal[] = [
  {
    id: 'foundation',
    title: 'Foundation',
    description: null,
    priority: 'High',
    dependsOn: [],
  },
  {
    id: 'feature',
    title: 'Feature',
    description: 'Build it',
    priority: 'Medium',
    dependsOn: ['foundation'],
  },
]

test('phase6 uses all six authenticated APIs with exact server contracts and scoped pagination', async () => {
  signin()
  const calls: { url: string; method: string; body: unknown }[] = []
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
    return Response.json({})
  }
  await dashboardApi.overview(scope)
  await dashboardApi.workload(scope, 2)
  await dashboardApi.overdue(scope, 3)
  await aiApi.propose(scope, 'A feature')
  await aiApi.accept(scope, {
    previewToken: 'opaque',
    accepted: true,
    tasks: proposals,
  })
  await aiApi.summary(scope)
  const base = `/api/v1/workspaces/${w}/projects/${p}`
  assert.deepEqual(calls, [
    { url: `${base}/dashboard`, method: 'GET', body: undefined },
    {
      url: `${base}/dashboard/workload?page=2&pageSize=20`,
      method: 'GET',
      body: undefined,
    },
    {
      url: `${base}/dashboard/overdue?page=3&pageSize=20`,
      method: 'GET',
      body: undefined,
    },
    {
      url: `${base}/ai/tasks/propose`,
      method: 'POST',
      body: { featureDescription: 'A feature' },
    },
    {
      url: `${base}/ai/tasks/accept`,
      method: 'POST',
      body: { previewToken: 'opaque', accepted: true, tasks: proposals },
    },
    { url: `${base}/ai/summary`, method: 'POST', body: undefined },
  ])
})

test('AI errors preserve fields and Retry-After, and never blindly retry generation or confirmation', async () => {
  signin()
  for (const status of [400, 403, 404, 409, 410, 429, 502, 503, 504]) {
    let calls = 0
    globalThis.fetch = async () => {
      calls++
      return Response.json(
        { errors: { 'tasks[1].title': ['Invalid'] } },
        { status, headers: { 'Retry-After': '17' } },
      )
    }
    for (const operation of [
      () => aiApi.propose(scope, 'feature'),
      () =>
        aiApi.accept(scope, {
          previewToken: 'token',
          accepted: true,
          tasks: proposals,
        }),
      () => aiApi.summary(scope),
    ]) {
      await assert.rejects(
        operation,
        (error: any) =>
          error.status === status &&
          error.fields['tasks[1].title'][0] === 'Invalid' &&
          (status !== 429 || error.retryAfterSeconds === 17),
      )
    }
    assert.equal(calls, 3)
    assert.ok(useSessionStore.getState().tokens)
  }
})

test('AI transport supports long-running requests and propagates explicit cancellation', async () => {
  signin()
  const controller = new AbortController()
  globalThis.fetch = async (_url, init) => {
    assert.ok(init?.signal)
    assert.equal(init.signal.aborted, false)
    const aborted = new Promise<Response>((_resolve, reject) =>
      init.signal!.addEventListener('abort', () =>
        reject(new Error('cancelled')),
      ),
    )
    controller.abort()
    return aborted
  }
  await assert.rejects(
    aiApi.summary(scope, controller.signal),
    (error: any) => error.status === 0,
  )
})

test('task preview editing validates trimmed titles, limits and dependency selection without mutating proposals', () => {
  assert.deepEqual(validateProposals(proposals), {})
  assert.match(validateProposals([]).tasks, /Select/)
  assert.match(
    validateProposals([proposals[1]])['tasks[0].dependsOn'],
    /other selected tasks/,
  )
  assert.match(
    validateProposals([{ ...proposals[0], title: '  ' }])['tasks[0].title'],
    /1–200/,
  )
  assert.match(
    validateProposals([{ ...proposals[0], title: 'a'.repeat(201) }])[
      'tasks[0].title'
    ],
    /1–200/,
  )
  assert.match(
    validateProposals([{ ...proposals[0], description: 'a'.repeat(20_001) }])[
      'tasks[0].description'
    ],
    /20,000/,
  )
  assert.match(
    validateProposals([{ ...proposals[0], priority: 'Invalid' } as any])[
      'tasks[0].priority'
    ],
    /valid priority/,
  )
  assert.match(
    validateProposals([{ ...proposals[0], dependsOn: ['foundation'] }])[
      'tasks[0].dependsOn'
    ],
    /other selected/,
  )
  assert.match(
    validateProposals([
      { ...proposals[0], dependsOn: ['feature'] },
      proposals[1],
    ]).tasks,
    /cycles/,
  )
  assert.match(
    validateProposals([
      proposals[0],
      { ...proposals[1], dependsOn: ['foundation', 'foundation'] },
    ])['tasks[1].dependsOn'],
    /unique/,
  )
  assert.match(
    validateProposals([proposals[0], { ...proposals[1], id: 'foundation' }])[
      'tasks[1].id'
    ],
    /unique/,
  )
  assert.deepEqual(proposals[1].dependsOn, ['foundation'])
  assert.deepEqual(
    validateProposals([{ ...proposals[0], title: ` ${'a'.repeat(200)} ` }]),
    {},
  )
})

test('dashboard and AI routes validate IDs and reject unsupported nested paths; old overview routes remain intact', () => {
  for (const section of ['dashboard', 'ai']) {
    assert.equal(
      parseRoute(`/workspaces/${w}/projects/${p}/${section}`).projectSection,
      section,
    )
    assert.equal(
      parseRoute(`/workspaces/${w}/projects/${p}/${section}/bad`).workspaceId,
      undefined,
    )
  }
  assert.equal(
    parseRoute(`/workspaces/${w}/projects/${p}`).projectSection,
    undefined,
  )
  assert.equal(
    parseRoute(`/workspaces/${w}/projects/bad/dashboard`).workspaceId,
    undefined,
  )
  assert.deepEqual(dashboardKey(scope), ['phase6', w, p])
  assert.ok(
    invalidateKeys(scope).some(
      (key) => JSON.stringify(key) === JSON.stringify(dashboardKey(scope)),
    ),
  )
})

function renderWithClient(
  element: ReturnType<typeof createElement>,
  seed?: (client: QueryClient) => void,
) {
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false, staleTime: Infinity } },
  })
  seed?.(client)
  const html = renderToStaticMarkup(
    createElement(QueryClientProvider, { client }, element),
  )
  client.clear()
  return html
}
const dashboard: Dashboard = {
  projectId: p,
  asOf: '2026-10-24T01:00:00Z',
  todayUtc: '2026-10-24',
  totalItems: 3,
  completedItems: 1,
  completionPercentage: 33.33,
  overdueItems: 1,
  countsByStatus: [
    { statusId: 'done', name: 'Finished', category: 'Done', count: 1 },
    { statusId: 'todo', name: 'Todo', category: 'Open', count: 2 },
  ],
  countsByPriority: [{ priority: 'Critical', count: 0 }],
  recentActivity: [],
}
test('dashboard uses authoritative server percentage, zero buckets, UTC dates and retained workload names', () => {
  const html = renderWithClient(
    createElement(ProjectDashboard, {
      scope,
      navigate: () => {},
      inspectWorkflows: false,
    }),
    (client) => {
      client.setQueryData([...dashboardKey(scope), 'overview'], dashboard)
      client.setQueryData(
        [...dashboardKey(scope), 'overdue', 1, dashboard.todayUtc],
        {
          items: [
            {
              id: 'item',
              key: 'WEB-1',
              title: '<script>bad()</script>',
              type: 'Task',
              status: 'Todo',
              priority: 'High',
              dueDate: '2026-10-23',
              assigneeUserId: null,
              estimateMinutes: null,
            },
          ],
          total: 1,
        },
      )
      client.setQueryData([...dashboardKey(scope), 'workload', 1], {
        items: [
          {
            assigneeUserId: null,
            displayName: 'Unassigned',
            openItems: 2,
            estimateMinutes: 6_000_000_000,
          },
        ],
        total: 1,
      })
    },
  )
  assert.match(html, /33.33%/)
  assert.match(html, /Finished/)
  assert.match(html, /Critical/)
  assert.match(html, /2026-10-24/)
  assert.match(html, /Unassigned/)
  assert.match(html, /6,000,000,000/)
  assert.match(html, /No estimate/)
  assert.match(html, /&lt;script&gt;/)
  assert.doesNotMatch(html, /<script>/)
  assert.match(html, /work-items\/item/)
})

test('AI task controls remain read only for Viewers and archived projects; summaries remain accessible', () => {
  for (const [role, archived] of [
    ['Viewer', false],
    ['Owner', true],
  ] as const) {
    const html = renderWithClient(
      createElement(AiAssistant, {
        workspace: { id: w, name: 'Team', role },
        project: {
          id: p,
          archivedAt: archived ? '2026-10-24T00:00:00Z' : null,
        } as any,
        navigate: () => {},
      }),
    )
    assert.match(html, /require write access/)
    assert.match(html, /<button[^>]*disabled=""[^>]*>Generate task proposals/)
    assert.doesNotMatch(html, /<button[^>]*disabled=""[^>]*>Generate summary/)
  }
})

test('proposal editor exposes named controls, selected dependencies and inline errors without interpreting markup', () => {
  const html = renderToStaticMarkup(
    createElement(ProposalEditor, {
      tasks: [
        { ...proposals[0], title: '<img src=x onerror=bad()>' },
        proposals[1],
      ],
      selected: ['feature'],
      errors: { 'tasks[0].dependsOn': 'Select foundation' },
      disabled: false,
      update: () => {},
      select: () => {},
    }),
  )
  assert.match(html, /Task 2 title/)
  assert.match(html, /Task 2 depends on/)
  assert.match(html, /Select foundation/)
  assert.match(html, /not selected/)
  assert.match(html, /aria-invalid="true"/)
  assert.match(html, /&lt;img/)
  assert.doesNotMatch(html, /<img/)
})

test('summary separates all-project facts from sampled AI interpretations and handles insufficient data', () => {
  const summary: ProjectSummary = {
    projectId: p,
    asOf: dashboard.asOf,
    todayUtc: dashboard.todayUtc,
    facts: {
      totalItems: 200,
      completedItems: 99,
      completionPercentage: 49.5,
      overdueItems: 2,
      blockedItems: 3,
    },
    status: 'Generated',
    narrative: {
      overview: '<script>bad()</script>',
      completedWork: 'Done',
      blockers: 'Unknown',
      overdueWork: 'Late',
      risks: ['<b>Risk</b>'],
    },
    isAiGenerated: true,
    contextItemCount: 50,
    contextTruncated: true,
  }
  const html = renderToStaticMarkup(
    createElement(SummaryView, { summary, stale: true }),
  )
  assert.match(html, /Facts from project data/)
  assert.match(html, /AI-generated interpretation/)
  assert.match(html, /49.5%/)
  assert.match(html, /only a sample/)
  assert.match(html, /data may have changed/)
  assert.match(html, /&lt;script&gt;/)
  assert.doesNotMatch(html, /<script>|<b>Risk/)
  const empty = renderToStaticMarkup(
    createElement(SummaryView, {
      summary: {
        ...summary,
        status: 'InsufficientData',
        narrative: null,
        isAiGenerated: false,
      },
      stale: false,
    }),
  )
  assert.match(empty, /not enough project data/)
  assert.doesNotMatch(empty, /AI-generated interpretation/)
})

test('AI error messages distinguish expiry, conflict, throttling, missing configuration, malformed output and timeout', () => {
  for (const [status, expected] of [
    [410, /expired/],
    [409, /different content/],
    [429, /17 seconds/],
    [503, /not been configured/],
    [502, /invalid response/],
    [504, /too long/],
  ] as const)
    assert.match(
      aiFailure(new ApiError('Safe error', status, {}, 17)),
      expected,
    )
})

test('confirmation failures describe uncertain writes separately from AI provider failures', () => {
  for (const status of [0, 502, 503, 504]) {
    assert.match(
      aiFailure(new ApiError('Safe failure', status), 'accept'),
      /Retry the same confirmation/,
    )
    assert.doesNotMatch(
      aiFailure(new ApiError('Safe failure', status), 'accept'),
      /configured|generat/,
    )
  }
  assert.match(
    aiFailure(new ApiError('Expired', 410), 'accept'),
    /Check Work items/,
  )
})

test('dashboard hides retained private data after a denied refetch and keeps workflow inspection permission separate', () => {
  const render = (inspectWorkflows: boolean, denied = false) =>
    renderWithClient(
      createElement(ProjectDashboard, {
        scope,
        navigate: () => {},
        inspectWorkflows,
      }),
      (client) => {
        const key = [...dashboardKey(scope), 'overview']
        client.setQueryData(key, {
          ...dashboard,
          recentActivity: [
            {
              id: 'activity',
              projectId: p,
              workItemId: 'item',
              action: 'work-item.archived',
              actorKind: 'System',
              actorUserId: null,
              occurredAt: dashboard.asOf,
              workflowExecutionId: 'execution',
              beforeValues: null,
              afterValues: null,
            },
          ],
        })
        if (denied)
          client
            .getQueryCache()
            .find({ queryKey: key })!
            .setState({
              error: new ApiError('Access denied', 404),
              status: 'error',
            })
      },
    )
  const hidden = render(false, true)
  assert.match(hidden, /Access denied/)
  assert.doesNotMatch(
    hidden,
    /33.33%|work item · archived|View workflow execution/,
  )
  assert.doesNotMatch(render(false), /View workflow execution/)
  assert.match(render(true), /View workflow execution/)
})
