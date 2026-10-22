import { afterEach, test } from 'node:test'
import assert from 'node:assert/strict'
import { accessToken, api } from '../src/lib/api.ts'
import { useSessionStore } from '../src/features/auth/session-store.ts'
import { notificationApi, notificationLabel, notificationPath, type Notification, type ProjectUpdate } from '../src/features/notifications/api.ts'
import { createLiveUpdates, eventWindow, type LiveConnection, type LiveState } from '../src/features/notifications/realtime.ts'

const fetchOriginal = globalThis.fetch
const signIn = (expired = false) => useSessionStore.getState().setTokens({ accessToken: 'access', refreshToken: 'refresh', accessTokenExpiresAt: new Date(Date.now() + (expired ? -1000 : 900000)).toISOString() })
afterEach(() => { globalThis.fetch = fetchOriginal; useSessionStore.getState().clear() })
const json = (value: unknown, status = 200) => new Response(JSON.stringify(value), { status, headers: { 'Content-Type': 'application/json' } })
const tick = async () => { for (let n = 0; n < 8; n++) await new Promise(resolve => setImmediate(resolve)) }
const n: Notification = { id: 'notice', type: 'Mentioned', workspaceId: 'workspace', projectId: 'project', workItemId: 'item', commentId: 'comment', invitationId: null, resourceUrl: 'https://untrusted.example/', createdAt: '2026-10-21T00:00:00Z', readAt: null }
test('notification pagination, unread total and bodyless read mutations follow the backend contract', async () => {
  signIn()
  const calls: string[] = []
  globalThis.fetch = async (url, init) => {
    calls.push(`${init?.method || 'GET'} ${url}`)
    assert.equal((init?.headers as Record<string, string> | undefined)?.Authorization, 'Bearer access')
    assert.equal(init?.body, undefined)
    return init?.method === 'POST' ? new Response(null, { status: 204 }) : json({ items: [n], total: 42, page: 1, pageSize: 1 })
  }
  assert.equal((await notificationApi.list(1, true, 1)).total, 42)
  await notificationApi.list(2)
  assert.equal(await notificationApi.read(n.id), undefined)
  assert.equal(await notificationApi.readAll(), undefined)
  assert.deepEqual(calls, ['GET /api/v1/notifications?page=1&pageSize=1&unreadOnly=true', 'GET /api/v1/notifications?page=2&pageSize=20&unreadOnly=false', 'POST /api/v1/notifications/notice/read', 'POST /api/v1/notifications/read-all'])
})
test('resource navigation ignores resourceUrl; invitations never invent tokens; unknown types are readable', () => {
  assert.equal(notificationPath(n), '/workspaces/workspace/projects/project/work-items/item#work-comments')
  assert.equal(notificationPath({ ...n, workItemId: null, invitationId: 'invite', projectId: null }), null)
  assert.equal(notificationLabel('Invited'), 'You were invited to a workspace')
  assert.equal(notificationLabel('Workflow'), 'A workflow sent you a notification')
  assert.equal(notificationLabel('FutureType'), 'You have a new notification')
})
test('notification mutation failures retain the session and are never blindly retried', async () => {
  for (const status of [403, 404, 503]) {
    signIn()
    let calls = 0
    globalThis.fetch = async () => { calls++; return json({ title: 'Unavailable' }, status) }
    await assert.rejects(notificationApi.read('missing'), { status })
    assert.equal(calls, 1)
    assert.ok(useSessionStore.getState().tokens)
  }
})
test('HTTP and SignalR share one refresh rotation and use the current token', async () => {
  signIn(true)
  let rotations = 0
  globalThis.fetch = async (url, init) => {
    if (String(url).endsWith('/auth/refresh')) {
      rotations++; await tick()
      return json({ accessToken: 'new-access', refreshToken: 'new-refresh', accessTokenExpiresAt: new Date(Date.now() + 900000).toISOString() })
    }
    assert.equal((init?.headers as Record<string, string> | undefined)?.Authorization, 'Bearer new-access')
    return json({ ok: true })
  }
  const [token] = await Promise.all([accessToken(), api('/notifications', {}, true), accessToken()])
  assert.equal(token, 'new-access'); assert.equal(rotations, 1)
})
test('invalid realtime refresh clears session and a late refresh cannot restore logout', async () => {
  signIn(true)
  globalThis.fetch = async () => json({ title: 'Invalid refresh' }, 401)
  await assert.rejects(accessToken(), { status: 401 })
  assert.equal(useSessionStore.getState().tokens, null)
  signIn(true)
  let complete!: (r: Response) => void
  globalThis.fetch = () => new Promise(resolve => { complete = resolve })
  const pending = accessToken()
  useSessionStore.getState().clear()
  complete(json({ accessToken: 'late', refreshToken: 'late-refresh', accessTokenExpiresAt: new Date(Date.now() + 900000).toISOString() }))
  await assert.rejects(pending, { status: 401 })
  assert.equal(useSessionStore.getState().tokens, null)
})
test('deduplication bounds memory and does not remember empty IDs', () => {
  const seen = eventWindow(2)
  assert.ok(seen('a')); assert.ok(!seen('a')); assert.ok(seen('b')); assert.ok(seen('c')); assert.ok(seen('a')); assert.ok(!seen(''))
})
class FakeConnection implements LiveConnection {
  calls: string[] = []
  handlers = new Map<string, (...args: any[]) => void>()
  closed = () => {}
  failStart = false
  failJoin = false
  startGate: Promise<void> | null = null
  joinGate: Promise<void> | null = null
  async start() { this.calls.push('start'); if (this.failStart) throw Error('Offline'); await this.startGate }
  async stop() { this.calls.push('stop') }
  async invoke(method: string, ...args: string[]) { this.calls.push(`${method}:${args.join('/')}`); if (method.startsWith('Join')) { if (this.failJoin) throw Error('No access'); await this.joinGate } }
  on(name: string, handler: (...args: any[]) => void) { this.handlers.set(name, handler) }
  off(name: string) { this.handlers.delete(name) }
  onclose(handler: () => void) { this.closed = handler }
  emit(name: string, event: unknown) { this.handlers.get(name)?.(event) }
}
function harness() {
  const connection = new FakeConnection()
  const states: LiveState[] = []
  const jobs: (() => void)[] = []
  const projects: ProjectUpdate[] = []
  let notifications = 0
  const live = createLiveUpdates(connection, {
    state: state => states.push(state), recover: async () => { connection.calls.push('recover') }, project: event => projects.push(event), notification: () => { notifications++ },
    schedule: run => { jobs.push(run); return jobs.length as unknown as ReturnType<typeof setTimeout> }, cancel: () => { jobs.length = 0 },
  })
  return { connection, states, jobs, projects, live, notifications: () => notifications }
}
const event: ProjectUpdate = { eventId: 'event', type: 'WorkItemUpdated', workspaceId: 'w', projectId: 'p', entityId: 'i', occurredAt: n.createdAt }
test('live subscriptions precede REST recovery and rejoin after connection loss', async () => {
  const h = harness(); h.live.setScope({ workspaceId: 'w', projectId: 'p' }); await tick()
  assert.deepEqual(h.connection.calls, ['start', 'JoinProject:w/p', 'recover'])
  h.connection.closed(); h.jobs.shift()!(); await tick()
  assert.deepEqual(h.connection.calls.slice(-3), ['start', 'JoinProject:w/p', 'recover'])
  assert.equal(h.states.at(-1), 'connected'); await h.live.stop()
})
test('initial start retries and denied subscriptions still refetch current access', async () => {
  const h = harness(); h.connection.failStart = true; h.live.setScope({ workspaceId: 'w' }); await tick()
  assert.equal(h.states.at(-1), 'offline'); assert.equal(h.jobs.length, 1)
  h.connection.failStart = false; h.connection.failJoin = true; h.jobs.shift()!(); await tick()
  assert.equal(h.connection.calls.at(-1), 'recover'); assert.equal(h.states.at(-1), 'offline')
  h.connection.failJoin = false; h.jobs.shift()!(); await tick()
  assert.equal(h.states.at(-1), 'connected'); await h.live.stop()
})
test('scope switching leaves old groups and ignores stale/duplicate events', async () => {
  const h = harness(); h.live.setScope({ workspaceId: 'w', projectId: 'p' }); await tick()
  h.connection.emit('ProjectUpdated', event); h.connection.emit('ProjectUpdated', event)
  h.connection.emit('NotificationReceived', { id: 'notice' }); h.connection.emit('NotificationReceived', { id: 'notice' })
  assert.equal(h.projects.length, 1); assert.equal(h.notifications(), 1)
  h.live.setScope({ workspaceId: 'other' }); await tick()
  assert.deepEqual(h.connection.calls.slice(-3), ['LeaveProject:p', 'JoinWorkspace:other', 'recover'])
  h.connection.emit('ProjectUpdated', { ...event, eventId: 'late' }); assert.equal(h.projects.length, 1)
  h.live.setScope({ workspaceId: 'other', projectId: 'q' }); await tick()
  assert.deepEqual(h.connection.calls.slice(-3), ['LeaveWorkspace:other', 'JoinProject:other/q', 'recover'])
  h.live.setScope({}); await tick()
  assert.deepEqual(h.connection.calls.slice(-2), ['LeaveProject:q', 'recover']); await h.live.stop()
})
test('rapid navigation during pending joins recovers only the final scope', async () => {
  const h = harness(); let release!: () => void
  h.connection.joinGate = new Promise(resolve => { release = resolve })
  h.live.setScope({ workspaceId: 'w', projectId: 'p' }); await tick()
  h.live.setScope({ workspaceId: 'w', projectId: 'new' }); release(); await tick()
  assert.deepEqual(h.connection.calls, ['start', 'JoinProject:w/p', 'LeaveProject:p', 'JoinProject:w/new', 'recover']); await h.live.stop()
})
test('cleanup during start stops late connections and removes event handlers', async () => {
  const h = harness(); let release!: () => void
  h.connection.startGate = new Promise(resolve => { release = resolve })
  h.live.start(); await tick(); await h.live.stop(); release(); await tick()
  assert.equal(h.connection.handlers.size, 0); assert.ok(!h.connection.calls.includes('recover')); assert.equal(h.connection.calls.at(-1), 'stop')
  h.connection.closed(); assert.equal(h.jobs.length, 0)
})
