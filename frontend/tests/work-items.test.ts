import { afterEach, test } from 'node:test'
import assert from 'node:assert/strict'
import {
  collectPages,
  workApi,
  type WorkItem,
  type WorkItemInput,
  type TimeEntry,
} from '../src/features/work-items/api.ts'
import {
  canModerate,
  canWriteWork,
  localDateTime,
  mentionParts,
  normalizeSearch,
  parentChoices,
  utcDateTime,
  timeInput,
  validateFile,
  workPath,
} from '../src/features/work-items/rules.ts'
import { parseRoute } from '../src/features/workspaces/rules.ts'
import { useSessionStore } from '../src/features/auth/session-store.ts'

const originalFetch = globalThis.fetch
const workspaceId = '10000000-0000-0000-0000-000000000001'
const projectId = '20000000-0000-0000-0000-000000000001'
const itemId = '30000000-0000-0000-0000-000000000001'
const scope = { workspaceId, projectId }
const root = `/api/v1/workspaces/${workspaceId}/projects/${projectId}`
const path = `${root}/work-items/${itemId}`
const input: WorkItemInput = {
  title: 'Task',
  description: null,
  type: 'Task',
  status: 'Todo',
  priority: 'Medium',
  assigneeUserId: null,
  parentId: null,
  startDate: '2026-10-21',
  dueDate: null,
  estimateMinutes: 0,
  labelIds: [],
}
function signIn() {
  useSessionStore.getState().setTokens({
    accessToken: 'access',
    refreshToken: 'refresh',
    accessTokenExpiresAt: new Date(Date.now() + 900000).toISOString(),
  })
}
afterEach(() => {
  globalThis.fetch = originalFetch
  useSessionStore.getState().clear()
})

test('work routes preserve deep links and reject misplaced or invalid nested IDs', () => {
  assert.deepEqual(parseRoute(workPath(workspaceId, projectId, itemId)), {
    workspaceId,
    section: 'projects',
    projectId,
    projectSection: 'work-items',
    itemId,
  })
  for (const section of ['labels', 'activity', 'work-items'])
    assert.equal(
      parseRoute(`/workspaces/${workspaceId}/projects/${projectId}/${section}`)
        .projectSection,
      section,
    )
  for (const suffix of [
    `labels/${itemId}`,
    'work-items/not-a-guid',
    'unknown',
    `activity/${itemId}`,
  ])
    assert.deepEqual(
      parseRoute(`/workspaces/${workspaceId}/projects/${projectId}/${suffix}`),
      { section: 'projects' },
    )
})
test('Developer can write work but Viewer and archived resources remain read only; moderation is separate', () => {
  for (const role of [
    'Owner',
    'Admin',
    'Manager',
    'Developer',
    'Viewer',
  ] as const) {
    assert.equal(canWriteWork(role, false), role !== 'Viewer')
    assert.equal(canWriteWork(role, true), false)
    assert.equal(canWriteWork(role, false, true), false)
    assert.equal(
      canModerate(role, 'me', 'someone'),
      ['Owner', 'Admin', 'Manager'].includes(role),
    )
    assert.equal(canModerate(role, 'me', 'me'), true)
  }
})
test('full-key search normalizes only keys in the current project', () => {
  assert.equal(normalizeSearch(' flow-003 ', 'FLOW'), '3')
  assert.equal(
    normalizeSearch('FLOW-9007199254740993', 'FLOW'),
    '9007199254740993',
  )
  assert.equal(normalizeSearch('FLOW-0000', 'FLOW'), '0')
  assert.equal(normalizeSearch('OTHER-3', 'FLOW'), 'OTHER-3')
  assert.equal(normalizeSearch('FLOW-three', 'FLOW'), 'FLOW-three')
  assert.equal(normalizeSearch(' Đội A&B ', 'FLOW'), 'Đội A&B')
})
test('parent choices exclude descendants beyond the first level and archived items', () => {
  const items = [
    { id: 'child', parentId: 'root' },
    { id: 'grandchild', parentId: 'child' },
    { id: 'root', parentId: null },
    { id: 'valid', parentId: null },
    { id: 'old', archivedAt: 'now' },
  ] as WorkItem[]
  assert.deepEqual(
    parentChoices(items, 'root').map((i) => i.id),
    ['valid'],
  )
})
test('mentions preserve plain text and exact member IDs without interpreting HTML', () => {
  assert.deepEqual(mentionParts(`<script> @{${itemId}} hi`), [
    { text: '<script> ', userId: undefined },
    { text: `@{${itemId}}`, userId: itemId },
    { text: ' hi', userId: undefined },
  ])
  assert.equal(mentionParts('@plainname')[0].userId, undefined)
})
test('time input round trips local times to UTC and keeps empty values nullable', () => {
  const iso = '2026-10-21T12:34:56.789Z'
  assert.equal(utcDateTime(localDateTime(iso)), iso)
  assert.equal(utcDateTime(''), null)
  assert.equal(localDateTime(null), '')
})
test('editing only a time-entry note preserves timestamp precision after native-input normalization', () => {
  for (const startedAt of [
    '2026-10-21T09:00:00.000123Z',
    '2026-10-21T09:12:34.100123Z',
    null,
  ]) {
    const original = { startedAt } as TimeEntry
    const data = new FormData()
    const browserValue = localDateTime(startedAt)
      .replace(/(\.\d*?[1-9])0+$/, '$1')
      .replace(/\.000$/, '')
      .replace(/:00$/, '')
    data.set('startedAt', browserValue)
    data.set('minutes', '45')
    data.set('note', ' Changed note ')
    assert.deepEqual(timeInput(data, original), {
      minutes: 45,
      startedAt,
      note: 'Changed note',
    })
  }
})
test('an unchanged local time retains the original instant in a repeated DST hour', () => {
  const previous = process.env.TZ
  try {
    process.env.TZ = 'America/New_York'
    const original = { startedAt: '2026-11-01T06:30:00.000123Z' } as TimeEntry
    const data = new FormData()
    data.set('minutes', '15')
    data.set('startedAt', '2026-11-01T01:30')
    assert.equal(timeInput(data, original).startedAt, original.startedAt)
    data.set('startedAt', '2026-11-01T03:00')
    assert.equal(
      timeInput(data, original).startedAt,
      '2026-11-01T08:00:00.000Z',
    )
    data.set('startedAt', '')
    assert.equal(timeInput(data, original).startedAt, null)
  } finally {
    if (previous === undefined) delete process.env.TZ
    else process.env.TZ = previous
  }
})
test('file validation enforces the backend MIME and size limits', () => {
  assert.equal(
    validateFile(new File(['notes'], 'notes.txt', { type: 'text/plain' })),
    null,
  )
  assert.ok(validateFile(new File([], 'empty.txt', { type: 'text/plain' })))
  assert.ok(
    validateFile(new File(['<html>'], 'page.html', { type: 'text/html' })),
  )
  assert.ok(
    validateFile(
      new File([new Uint8Array(10 * 1024 * 1024 + 1)], 'large.pdf', {
        type: 'application/pdf',
      }),
    ),
  )
})
test('all JSON work/collaboration operations use existing routes and exact DTO fields', async () => {
  signIn()
  const calls: unknown[] = []
  globalThis.fetch = async (url, init) => {
    assert.equal(
      (init?.headers as Record<string, string> | undefined)?.Authorization,
      'Bearer access',
    )
    calls.push([
      String(url),
      init?.method || 'GET',
      init?.body ? JSON.parse(String(init.body)) : undefined,
    ])
    return new Response(null, { status: 204 })
  }
  await workApi.statuses(scope)
  await workApi.labels(scope)
  await workApi.saveLabel(scope, { name: 'Bug', color: null })
  await workApi.saveLabel(scope, { name: 'Bug', color: '#123456' }, 'label')
  await workApi.deleteLabel(scope, 'label')
  await workApi.create(scope, input)
  await workApi.get(scope, itemId)
  await workApi.update(scope, itemId, input, 7)
  await workApi.archive(scope, itemId, true)
  await workApi.archive(scope, itemId, false)
  await workApi.addDependency(scope, itemId, 'dependency')
  await workApi.removeDependency(scope, itemId, 'dependency')
  await workApi.comments(scope, itemId, 2)
  await workApi.saveComment(scope, itemId, 'Hello')
  await workApi.saveComment(scope, itemId, 'Edited', {
    id: 'comment',
    version: 3,
  })
  await workApi.deleteComment(scope, itemId, 'comment')
  await workApi.timeEntries(scope, itemId, 2)
  const time = { minutes: 45, startedAt: '2026-10-21T01:00:00Z', note: null }
  await workApi.saveTime(scope, itemId, time)
  await workApi.saveTime(scope, itemId, time, 'entry')
  await workApi.deleteTime(scope, itemId, 'entry')
  await workApi.attachments(scope, itemId, 2)
  await workApi.deleteAttachment(scope, itemId, 'file')
  await workApi.activity(scope, 2, itemId)
  assert.deepEqual(calls, [
    [`${root}/statuses`, 'GET', undefined],
    [`${root}/labels`, 'GET', undefined],
    [`${root}/labels`, 'POST', { name: 'Bug', color: null }],
    [`${root}/labels/label`, 'PUT', { name: 'Bug', color: '#123456' }],
    [`${root}/labels/label`, 'DELETE', undefined],
    [`${root}/work-items`, 'POST', input],
    [path, 'GET', undefined],
    [path, 'PUT', { ...input, version: 7 }],
    [`${path}/archive`, 'POST', undefined],
    [`${path}/restore`, 'POST', undefined],
    [`${path}/dependencies`, 'POST', { dependsOnItemId: 'dependency' }],
    [`${path}/dependencies/dependency`, 'DELETE', undefined],
    [`${path}/comments?page=2&pageSize=20`, 'GET', undefined],
    [`${path}/comments`, 'POST', { body: 'Hello' }],
    [`${path}/comments/comment`, 'PUT', { body: 'Edited', version: 3 }],
    [`${path}/comments/comment`, 'DELETE', undefined],
    [`${path}/time-entries?page=2&pageSize=20`, 'GET', undefined],
    [`${path}/time-entries`, 'POST', time],
    [`${path}/time-entries/entry`, 'PUT', time],
    [`${path}/time-entries/entry`, 'DELETE', undefined],
    [`${path}/attachments?page=2&pageSize=20`, 'GET', undefined],
    [`${path}/attachments/file`, 'DELETE', undefined],
    [
      `/api/v1/workspaces/${workspaceId}/activity?projectId=${projectId}&page=2&pageSize=20&workItemId=${itemId}`,
      'GET',
      undefined,
    ],
  ])
})
test('server filters encode Unicode, ampersands, dates, booleans and pagination', async () => {
  signIn()
  globalThis.fetch = async (url) => {
    const query = new URL(String(url), 'http://localhost').searchParams
    assert.equal(query.get('search'), 'Đội A&B /')
    for (const [key, value] of Object.entries({
      status: 'InReview',
      type: 'Issue',
      priority: 'Critical',
      assigneeUserId: itemId,
      reporterUserId: itemId,
      labelId: itemId,
      dueBefore: '2026-11-01',
      dueAfter: '2026-10-21',
      archived: 'false',
      sort: 'priority',
      descending: 'false',
      page: '2',
      pageSize: '20',
    }))
      assert.equal(query.get(key), value)
    return new Response(
      JSON.stringify({ items: [], total: 0, page: 2, pageSize: 20 }),
    )
  }
  await workApi.list(scope, {
    search: 'Đội A&B /',
    status: 'InReview',
    type: 'Issue',
    priority: 'Critical',
    assigneeUserId: itemId,
    reporterUserId: itemId,
    labelId: itemId,
    dueBefore: '2026-11-01',
    dueAfter: '2026-10-21',
    archived: 'false',
    sort: 'priority',
    descending: false,
    page: 2,
  })
})
test('multipart uploads and binary downloads retain auth and survive a 401 refresh', async () => {
  signIn()
  let uploadCalls = 0
  globalThis.fetch = async (url, init) => {
    if (String(url).endsWith('/auth/refresh'))
      return new Response(
        JSON.stringify({
          accessToken: 'next',
          refreshToken: 'next-refresh',
          accessTokenExpiresAt: new Date(Date.now() + 900000).toISOString(),
        }),
      )
    const headers = init?.headers as Record<string, string>
    if (init?.method === 'POST') {
      assert.ok(init.body instanceof FormData)
      assert.equal(headers['Content-Type'], undefined)
      assert.equal((init.body.get('file') as File).name, 'note.txt')
      if (++uploadCalls === 1) return new Response('{}', { status: 401 })
      assert.equal(headers.Authorization, 'Bearer next')
      return new Response('{"id":"file"}')
    }
    assert.equal(headers.Authorization, 'Bearer next')
    return new Response('binary text', {
      headers: { 'Content-Type': 'text/plain' },
    })
  }
  assert.equal(
    (
      await workApi.upload(
        scope,
        itemId,
        new File(['hello'], 'note.txt', { type: 'text/plain' }),
      )
    ).id,
    'file',
  )
  assert.equal(
    await (await workApi.download(scope, itemId, 'file')).text(),
    'binary text',
  )
  assert.equal(uploadCalls, 2)
})
test('all-page pickers include later project members plus implicit Owners/Admins without duplicates', async () => {
  signIn()
  const owner = { userId: 'owner', role: 'Owner', displayName: 'Owner' }
  const admin = { userId: 'admin', role: 'Admin', displayName: 'Admin' }
  const developer = {
    userId: 'developer',
    role: 'Developer',
    displayName: 'Developer',
  }
  globalThis.fetch = async (url) => {
    const request = new URL(String(url), 'http://localhost')
    if (request.pathname === `/api/v1/workspaces/${workspaceId}/members`)
      return new Response(
        JSON.stringify([
          owner,
          admin,
          developer,
          { userId: 'outsider', role: 'Developer' },
        ]),
      )
    const page = Number(request.searchParams.get('page'))
    return new Response(
      JSON.stringify({
        items: page === 1 ? [owner] : [developer],
        total: 2,
        page,
        pageSize: 1,
      }),
    )
  }
  assert.deepEqual(
    (await workApi.members(scope)).map((m) => m.userId),
    ['owner', 'admin', 'developer'],
  )
})
test('relation discovery reads every active and archived page; incomplete pagination fails visibly', async () => {
  signIn()
  const calls: string[] = []
  globalThis.fetch = async (url) => {
    const query = new URL(String(url), 'http://localhost').searchParams
    const state = `${query.get('archived')}-${query.get('page')}`
    calls.push(state)
    return new Response(
      JSON.stringify({
        items: [{ id: state }],
        total: 2,
        page: Number(query.get('page')),
        pageSize: 1,
      }),
    )
  }
  assert.equal((await workApi.relations(scope)).length, 4)
  assert.deepEqual(calls.sort(), ['false-1', 'false-2', 'true-1', 'true-2'])
  await assert.rejects(
    collectPages(async (page) => ({ items: [], total: 10, page, pageSize: 2 })),
    /list changed/,
  )
})
test('work and comment conflicts, permission failures and unavailable files propagate without blind write retries', async () => {
  signIn()
  for (const status of [400, 403, 404, 409]) {
    let calls = 0
    globalThis.fetch = async () => {
      calls++
      return new Response(
        JSON.stringify({
          title: 'Changed or unavailable',
          errors: { title: ['Invalid'] },
        }),
        { status },
      )
    }
    await assert.rejects(workApi.update(scope, itemId, input, 1), { status })
    assert.equal(calls, 1)
    assert.ok(useSessionStore.getState().tokens)
  }
})
