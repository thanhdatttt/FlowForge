// Explicitly disposable local API + PostgreSQL. Runs actual frontend clients,
// lifecycle controller and SignalR client through Vite's WebSocket proxy.
import assert from 'node:assert/strict'
import { randomUUID, createHmac } from 'node:crypto'
import { mkdirSync, writeFileSync } from 'node:fs'
import { HubConnectionBuilder, LogLevel } from '@microsoft/signalr'
import { authApi } from '../src/features/auth/api.ts'
import { useSessionStore, type AuthTokens } from '../src/features/auth/session-store.ts'
import { accessToken } from '../src/lib/api.ts'
import { projectApi, workspaceApi } from '../src/features/workspaces/api.ts'
import { workApi, type Scope, type WorkItemInput } from '../src/features/work-items/api.ts'
import { itemInput } from '../src/features/work-items/rules.ts'
import { notificationApi } from '../src/features/notifications/api.ts'
import { createLiveUpdates, type LiveState } from '../src/features/notifications/realtime.ts'
const origin = process.env.FLOWFORGE_LIVE_ORIGIN
const signingKey = process.env.FLOWFORGE_TEST_SIGNING_KEY
if (!origin || !signingKey || process.env.FLOWFORGE_PHASE4_DISPOSABLE !== '1' || !['localhost', '127.0.0.1'].includes(new URL(origin).hostname)) throw Error('Explicit localhost disposable phase4 origin and test signing key required.')
const nativeFetch = globalThis.fetch
let requests = 0
globalThis.fetch = (url, init) => { requests++; return nativeFetch(new URL(String(url), origin), init) }
const output = new URL('../test-results/phase4/', import.meta.url)
mkdirSync(output, { recursive: true })
const checks: string[] = []
async function check(name: string, run: () => Promise<void>) { await run(); checks.push(name); console.log(`PASS ${name}`) }
const delay = (ms: number) => new Promise(resolve => setTimeout(resolve, ms))
async function until(check: () => Promise<boolean> | boolean, timeout = 15000) {
  const end = Date.now() + timeout
  while (Date.now() < end) { if (await check()) return; await delay(200) }
  throw Error('Timed out waiting for live state')
}
const password = 'Phase4-test-password!2026'
const suffix = randomUUID().slice(0, 8)
const accounts: Record<string, { email: string; id: string; tokens: AuthTokens }> = {}
let scope!: Scope
let itemId = ''
let invitationId = ''
let invitationLink = ''
let controller: ReturnType<typeof createLiveUpdates> | undefined
let state: LiveState = 'connecting'
let recoveries = 0
let notificationEvents = 0
let projectEvents = 0
const as = (name: string) => useSessionStore.getState().setTokens(accounts[name].tokens)
const input: WorkItemInput = { title: 'Live phase four task', description: 'Disposable frontend verification', type: 'Task', status: 'Todo', priority: 'Medium', assigneeUserId: null, parentId: null, startDate: null, dueDate: null, estimateMinutes: null, labelIds: [] }
try {
  await check('real accounts, authorized project and separate invitation recipient', async () => {
    for (const name of ['owner', 'member', 'invited', 'outsider']) {
      const email = `phase4-${name}-${suffix}@example.test`
      const tokens = await authApi.register({ email, password, displayName: `Phase 4 ${name}` })
      useSessionStore.getState().setTokens(tokens)
      accounts[name] = { email, tokens, id: (await authApi.me()).id }
    }
    as('owner')
    const w = await workspaceApi.create(`Phase 4 verification ${suffix}`)
    await workspaceApi.addMember(w.id, accounts.member.email, 'Developer')
    const p = await projectApi.create(w.id, { name: 'Realtime', projectKey: 'PFOUR', description: null, leadUserId: null, startsOn: null, targetOn: null })
    scope = { workspaceId: w.id, projectId: p.id }
    await projectApi.addMember(w.id, p.id, accounts.member.id)
  })
  const connection = new HubConnectionBuilder().withUrl(`${origin}/hubs/updates`, { accessTokenFactory: accessToken }).configureLogging(LogLevel.None).build()
  await check('short-lived authenticated connection joins before REST recovery', async () => {
    as('member')
    const tokens = accounts.member.tokens
    const [header, encoded] = tokens.accessToken.split('.')
    const expires = Math.floor(Date.now() / 1000) + 35
    const payload = Buffer.from(JSON.stringify({ ...JSON.parse(Buffer.from(encoded, 'base64url').toString()), exp: expires })).toString('base64url')
    const data = `${header}.${payload}`
    const signed = `${data}.${createHmac('sha256', signingKey!).update(data).digest('base64url')}`
    useSessionStore.getState().setTokens({ ...tokens, accessToken: signed, accessTokenExpiresAt: new Date(expires * 1000).toISOString() })
    controller = createLiveUpdates(connection, {
      state: next => { state = next },
      recover: async () => { await workApi.list(scope); await notificationApi.list(); recoveries++ },
      project: () => { projectEvents++ }, notification: () => { notificationEvents++ },
    })
    controller.setScope(scope)
    await until(() => state === 'connected')
    assert.equal(recoveries, 1)
  })
  await check('assignment and mention reach the recipient via SignalR and authorized REST', async () => {
    // Owner writes use native fetch and do not replace the connected member's session.
    const ownerPost = async (path: string, body: unknown) => {
      const response = await nativeFetch(`${origin}/api/v1${path}`, { method: 'POST', headers: { Authorization: `Bearer ${accounts.owner.tokens.accessToken}`, 'Content-Type': 'application/json' }, body: JSON.stringify(body) })
      assert.ok(response.ok); return response.json()
    }
    const item = await ownerPost(`/workspaces/${scope.workspaceId}/projects/${scope.projectId}/work-items`, { ...input, assigneeUserId: accounts.member.id })
    itemId = item.id
    await ownerPost(`/workspaces/${scope.workspaceId}/projects/${scope.projectId}/work-items/${itemId}/comments`, { body: `Please review @{${accounts.member.id}}` })
    await until(() => notificationEvents >= 2 && projectEvents >= 2)
    const unread = await notificationApi.list(1, true, 1)
    assert.equal(unread.total, 2); assert.equal(unread.items.length, 1)
    const all = await notificationApi.list()
    assert.deepEqual(all.items.map(n => n.type).sort(), ['Assigned', 'Mentioned'])
  })
  await check('read-one and read-all remain idempotent and recipient isolated', async () => {
    const first = (await notificationApi.list()).items[0]
    await notificationApi.read(first.id); await notificationApi.read(first.id)
    assert.equal((await notificationApi.list(1, true)).total, 1)
    await notificationApi.readAll(); await notificationApi.readAll()
    assert.equal((await notificationApi.list(1, true)).total, 0)
    const denied = await nativeFetch(`${origin}/api/v1/notifications/${first.id}/read`, { method: 'POST', headers: { Authorization: `Bearer ${accounts.outsider.tokens.accessToken}` } })
    assert.equal(denied.status, 404)
  })
  await check('server token-expiry disconnect refreshes once and rejoins/refetches', async () => {
    await until(() => recoveries >= 2 && state === 'connected', 45000)
    assert.notEqual(useSessionStore.getState().tokens?.refreshToken, accounts.member.tokens.refreshToken)
    accounts.member.tokens = useSessionStore.getState().tokens!
  })
  await check('transport interruption rejoins and recovers current REST versions', async () => {
    const before = recoveries
    await connection.stop()
    const response = await nativeFetch(`${origin}/api/v1/workspaces/${scope.workspaceId}/projects/${scope.projectId}/work-items/${itemId}`, { headers: { Authorization: `Bearer ${accounts.owner.tokens.accessToken}` } })
    const item = await response.json()
    const updated = await nativeFetch(`${origin}/api/v1/workspaces/${scope.workspaceId}/projects/${scope.projectId}/work-items/${itemId}`, { method: 'PUT', headers: { Authorization: `Bearer ${accounts.owner.tokens.accessToken}`, 'Content-Type': 'application/json' }, body: JSON.stringify({ ...itemInput(item), title: 'Changed while disconnected', version: item.version }) })
    assert.ok(updated.ok)
    await until(() => recoveries > before && state === 'connected')
    assert.equal((await workApi.get(scope, itemId)).title, 'Changed while disconnected')
  })
  await controller.stop(); controller = undefined
  await check('invitation notification exposes metadata without token; revoked invitations disappear', async () => {
    as('owner')
    const invitation = await workspaceApi.invite(scope.workspaceId, accounts.invited.email, 'Viewer')
    as('invited')
    await until(async () => (await notificationApi.list()).total === 1)
    const n = (await notificationApi.list()).items[0]
    assert.equal(n.type, 'Invited'); assert.equal(n.invitationId, invitation.invitation.id)
    assert.ok(!('token' in n)); assert.ok(!n.resourceUrl.includes('#'))
    as('owner'); await workspaceApi.revoke(scope.workspaceId, invitation.invitation.id)
    as('invited'); assert.equal((await notificationApi.list()).total, 0)
    as('owner')
    const fresh = await workspaceApi.invite(scope.workspaceId, accounts.invited.email, 'Viewer')
    invitationId = fresh.invitation.id; invitationLink = fresh.invitationUrl
  })
  await check('removed project access hides historical notifications and denies subscription', async () => {
    as('owner'); await projectApi.removeMember(scope.workspaceId, scope.projectId, accounts.member.id)
    as('member'); assert.equal((await notificationApi.list()).total, 0)
    const denied = new HubConnectionBuilder().withUrl(`${origin}/hubs/updates`, { accessTokenFactory: accessToken }).configureLogging(LogLevel.None).build()
    try { await denied.start(); await assert.rejects(denied.invoke('JoinProject', scope.workspaceId, scope.projectId)) } finally { await denied.stop() }
    as('owner'); await projectApi.addMember(scope.workspaceId, scope.projectId, accounts.member.id)
  })
  await check('overdue worker creates the correct recipient reminder', async () => {
    as('owner')
    await workApi.create(scope, { ...input, title: 'Overdue verification', assigneeUserId: accounts.member.id, dueDate: '2020-01-01' })
    as('member')
    await until(async () => (await notificationApi.list()).items.some(n => n.type === 'Overdue'), 70000)
  })
  writeFileSync('/tmp/flowforge-phase4-browser.json', JSON.stringify({ scope, itemId, invitationId, invitationLink, password, accounts: Object.fromEntries(Object.entries(accounts).map(([name, a]) => [name, { id: a.id, email: a.email }])) }, null, 2), { mode: 0o600 })
} finally {
  await controller?.stop()
  globalThis.fetch = nativeFetch
  useSessionStore.getState().clear()
  writeFileSync(new URL('live-backend.json', output), JSON.stringify({ executedAt: new Date().toISOString(), checks, requests, notificationEvents, projectEvents, recoveries }, null, 2))
  console.log(`${checks.length} scenarios passed, ${requests} requests`)
}
