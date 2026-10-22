import { afterEach, test } from 'node:test'
import assert from 'node:assert/strict'
import {
  invitationApi,
  projectApi,
  workspaceApi,
  type Role,
} from '../src/features/workspaces/api.ts'
import {
  assignableRoles,
  canManageMember,
  frontendInvitationLink,
  managesProject,
  managesWorkspace,
  parseInvitationLink,
  parseRoute,
} from '../src/features/workspaces/rules.ts'
import { useSessionStore } from '../src/features/auth/session-store.ts'

const originalFetch = globalThis.fetch
const workspaceId = '10000000-0000-0000-0000-000000000001'
const projectId = '20000000-0000-0000-0000-000000000001'
const userId = '30000000-0000-0000-0000-000000000001'
const invitationId = '40000000-0000-0000-0000-000000000001'
afterEach(() => {
  globalThis.fetch = originalFetch
  useSessionStore.getState().clear()
})

for (const role of [
  'Owner',
  'Admin',
  'Manager',
  'Developer',
  'Viewer',
] as Role[]) {
  test(`${role} UI controls reflect workspace and project permission boundaries`, () => {
    assert.equal(managesWorkspace(role), ['Owner', 'Admin'].includes(role))
    assert.equal(
      managesProject(role),
      ['Owner', 'Admin', 'Manager'].includes(role),
    )
    assert.equal(canManageMember(role, 'Owner'), false)
    assert.equal(canManageMember(role, 'Admin'), role === 'Owner')
    assert.equal(
      canManageMember(role, 'Developer'),
      ['Owner', 'Admin'].includes(role),
    )
    assert.ok(!assignableRoles(role).includes('Owner'))
  })
}
test('frontend and existing API invitation links round trip tokens in fragments only', () => {
  const invite = { id: invitationId, token: 'opaque+/token==' }
  const frontend = frontendInvitationLink('https://app.example.test', invite)
  assert.deepEqual(parseInvitationLink(frontend), invite)
  assert.deepEqual(
    parseInvitationLink(
      `https://api.example.test/api/v1/invitations/${invitationId}#token=opaque%2B%2Ftoken%3D%3D`,
    ),
    invite,
  )
  assert.equal(new URL(frontend).search, '')
  assert.equal(new URL(frontend).pathname.includes(invite.token), false)
})
test('invalid, missing and query-string invitation tokens are rejected', () => {
  for (const link of [
    `javascript:alert(1)#token=x`,
    `/invitations/bad#token=x`,
    `/invitations/${invitationId}?token=secret`,
    `/invitations/${invitationId}`,
    `/invitations/${invitationId}#token=${'x'.repeat(257)}`,
    `/invitations/${invitationId}#token=has%20space`,
    `/workspaces/${workspaceId}#token=secret`,
  ])
    assert.equal(parseInvitationLink(link), null)
})
test('workspace routes validate nested project IDs and preserve section identity', () => {
  assert.deepEqual(
    parseRoute(`/workspaces/${workspaceId}/projects/${projectId}`),
    { workspaceId, section: 'projects', projectId },
  )
  assert.equal(
    parseRoute(`/workspaces/${workspaceId}/members`).section,
    'members',
  )
  assert.deepEqual(
    parseRoute(`/workspaces/${workspaceId}/members/${projectId}`),
    { section: 'projects' },
  )
  assert.deepEqual(parseRoute('/workspaces/not-a-guid/projects'), {
    section: 'projects',
  })
})

test('all Phase 2 operations use authenticated existing backend routes, payloads and pagination', async () => {
  useSessionStore.getState().setTokens({
    accessToken: 'access',
    refreshToken: 'refresh',
    accessTokenExpiresAt: new Date(Date.now() + 900_000).toISOString(),
  })
  const base = `/api/v1/workspaces/${workspaceId}`
  const project = `${base}/projects/${projectId}`
  const input = {
    name: 'Team',
    projectKey: 'TEAM',
    description: null,
    leadUserId: userId,
    startsOn: '2026-10-20',
    targetOn: null,
  }
  const calls: Array<[string, string, unknown]> = []
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
  const signal = new AbortController().signal
  await workspaceApi.list(signal)
  await workspaceApi.get(workspaceId, signal)
  await workspaceApi.create('Team')
  await workspaceApi.rename(workspaceId, 'New name')
  await workspaceApi.members(workspaceId)
  await workspaceApi.addMember(workspaceId, 'member@example.test', 'Developer')
  await workspaceApi.changeRole(workspaceId, userId, 'Viewer')
  await workspaceApi.removeMember(workspaceId, userId)
  await workspaceApi.transfer(workspaceId, userId)
  await workspaceApi.invitations(workspaceId, 2)
  await workspaceApi.invite(workspaceId, 'invite@example.test', 'Manager')
  await workspaceApi.revoke(workspaceId, invitationId)
  await invitationApi.get(invitationId)
  await invitationApi.respond('opaque-secret', true)
  await invitationApi.respond('opaque-secret', false)
  await projectApi.get(workspaceId, projectId)
  await projectApi.create(workspaceId, input)
  await projectApi.update(workspaceId, projectId, input, 7)
  await projectApi.archive(workspaceId, projectId, true)
  await projectApi.archive(workspaceId, projectId, false)
  await projectApi.members(workspaceId, projectId, 3)
  await projectApi.addMember(workspaceId, projectId, userId)
  await projectApi.removeMember(workspaceId, projectId, userId)
  assert.deepEqual(calls, [
    ['/api/v1/workspaces', 'GET', undefined],
    [base, 'GET', undefined],
    ['/api/v1/workspaces', 'POST', { name: 'Team' }],
    [base, 'PUT', { name: 'New name' }],
    [`${base}/members`, 'GET', undefined],
    [
      `${base}/members`,
      'POST',
      { email: 'member@example.test', role: 'Developer' },
    ],
    [`${base}/members/${userId}/role`, 'PUT', { role: 'Viewer' }],
    [`${base}/members/${userId}`, 'DELETE', undefined],
    [`${base}/ownership/transfer`, 'POST', { newOwnerUserId: userId }],
    [`${base}/invitations?page=2&pageSize=20`, 'GET', undefined],
    [
      `${base}/invitations`,
      'POST',
      { email: 'invite@example.test', role: 'Manager' },
    ],
    [`${base}/invitations/${invitationId}/revoke`, 'POST', undefined],
    [`/api/v1/invitations/${invitationId}`, 'GET', undefined],
    ['/api/v1/invitations/accept', 'POST', { token: 'opaque-secret' }],
    ['/api/v1/invitations/reject', 'POST', { token: 'opaque-secret' }],
    [project, 'GET', undefined],
    [`${base}/projects`, 'POST', input],
    [project, 'PUT', { ...input, version: 7 }],
    [`${project}/archive`, 'POST', undefined],
    [`${project}/restore`, 'POST', undefined],
    [`${project}/members?page=3&pageSize=20`, 'GET', undefined],
    [`${project}/members`, 'POST', { userId }],
    [`${project}/members/${userId}`, 'DELETE', undefined],
  ])
})
test('project filters encode user input and omit unset filters instead of inventing defaults', async () => {
  useSessionStore.getState().setTokens({
    accessToken: 'access',
    refreshToken: 'refresh',
    accessTokenExpiresAt: new Date(Date.now() + 900_000).toISOString(),
  })
  const urls: URL[] = []
  globalThis.fetch = async (url) => {
    urls.push(new URL(String(url), 'http://localhost'))
    return new Response('{"items":[],"total":0,"page":1,"pageSize":20}')
  }
  await projectApi.list(workspaceId, {
    search: 'A&B / Đội',
    archived: 'true',
    leadUserId: userId,
    sort: 'updatedAt',
    descending: true,
    page: 2,
  })
  assert.equal(urls[0].searchParams.get('search'), 'A&B / Đội')
  assert.equal(urls[0].searchParams.get('archived'), 'true')
  assert.equal(urls[0].searchParams.get('leadUserId'), userId)
  assert.equal(urls[0].searchParams.get('descending'), 'true')
  assert.equal(urls[0].searchParams.get('page'), '2')
  await projectApi.list(workspaceId, {
    search: '',
    archived: '',
    leadUserId: '',
    sort: 'name',
    descending: false,
    page: 1,
  })
  assert.equal(urls[1].searchParams.has('archived'), false)
  assert.equal(urls[1].searchParams.has('leadUserId'), false)
  assert.equal(urls[1].searchParams.has('search'), false)
})
test('permission and version conflicts propagate without retrying writes or clearing the session', async () => {
  useSessionStore.getState().setTokens({
    accessToken: 'access',
    refreshToken: 'refresh',
    accessTokenExpiresAt: new Date(Date.now() + 900_000).toISOString(),
  })
  for (const status of [403, 404, 409]) {
    let calls = 0
    globalThis.fetch = async () => {
      calls++
      return new Response(
        JSON.stringify({ title: 'Resource changed or unavailable.' }),
        { status },
      )
    }
    await assert.rejects(projectApi.archive(workspaceId, projectId, true), {
      status,
    })
    assert.equal(calls, 1)
    assert.notEqual(useSessionStore.getState().tokens, null)
  }
})
