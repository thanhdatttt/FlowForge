// Runs actual frontend clients through Vite against a disposable real backend.
// See docs/phase2_frontend.md for the required isolated environment.
import assert from 'node:assert/strict'
import { createHmac, randomUUID } from 'node:crypto'
import { execFileSync } from 'node:child_process'
import { mkdirSync, writeFileSync } from 'node:fs'
import { authApi } from '../src/features/auth/api.ts'
import {
  useSessionStore,
  type AuthTokens,
} from '../src/features/auth/session-store.ts'
import {
  invitationApi,
  projectApi,
  workspaceApi,
  type Role,
  type ProjectFilters,
  type ProjectInput,
} from '../src/features/workspaces/api.ts'
import {
  frontendInvitationLink,
  parseInvitationLink,
} from '../src/features/workspaces/rules.ts'
import { ApiError } from '../src/lib/api.ts'

const origin = process.env.FLOWFORGE_LIVE_ORIGIN
const container = process.env.FLOWFORGE_TEST_DB_CONTAINER
const database = process.env.FLOWFORGE_TEST_DB_NAME
const signingKey = process.env.FLOWFORGE_TEST_SIGNING_KEY
if (
  !origin ||
  !container?.startsWith('flowforge-fe-audit-db-') ||
  !database?.startsWith('flowforge_frontend_') ||
  !signingKey
)
  throw new Error(
    'Explicit localhost test origin, disposable audit container/database and test signing key are required.',
  )
if (!['localhost', '127.0.0.1'].includes(new URL(origin).hostname))
  throw new Error('Live audit must use localhost.')
const output = new URL('../test-results/phase2/', import.meta.url)
mkdirSync(output, { recursive: true })
const http: Array<{ method: string; path: string; status: number }> = []
const checks: Array<{ name: string; passed: boolean; error?: string }> = []
const nativeFetch = globalThis.fetch
// Only resolve relative URLs; every response still comes from real HTTP.
globalThis.fetch = async (input, init) => {
  const url = new URL(String(input), origin)
  const response = await nativeFetch(url, init)
  http.push({
    method: init?.method || 'GET',
    path: url.pathname,
    status: response.status,
  })
  return response
}
const quote = (value: string) => `'${value.replaceAll("'", "''")}'`
function sql(command: string) {
  return execFileSync(
    'docker',
    [
      'exec',
      '-i',
      container!,
      'psql',
      '-U',
      'postgres',
      '-d',
      database!,
      '-v',
      'ON_ERROR_STOP=1',
      '-At',
    ],
    { input: command, encoding: 'utf8', stdio: ['pipe', 'pipe', 'pipe'] },
  ).trim()
}
async function check(name: string, run: () => unknown | Promise<unknown>) {
  try {
    await run()
    checks.push({ name, passed: true })
    console.log(`PASS ${name}`)
  } catch (error) {
    checks.push({
      name,
      passed: false,
      error: error instanceof Error ? error.message : String(error),
    })
    throw error
  }
}
async function rejects(
  run: () => Promise<unknown>,
  status: number,
  fields?: string[],
) {
  await assert.rejects(
    run(),
    (error: unknown) =>
      error instanceof ApiError &&
      error.status === status &&
      (!fields || fields.every((field) => field in error.fields)),
  )
}
const suffix = randomUUID().slice(0, 8)
const password = 'AuditPassword1!'
const accounts: Record<
  string,
  { id: string; email: string; tokens: AuthTokens }
> = {}
function as(name: string) {
  useSessionStore.getState().setTokens(accounts[name].tokens)
}
function save(name: string) {
  accounts[name].tokens = useSessionStore.getState().tokens!
}
async function verify(name: string) {
  const account = accounts[name]
  const id = randomUUID()
  const code = '012345'
  const hash = createHmac('sha256', signingKey!)
    .update(
      `flowforge:email-verification:${id.replaceAll('-', '')}:${account.id.replaceAll('-', '')}:${code}`,
    )
    .digest('hex')
    .toUpperCase()
  sql(
    `INSERT INTO email_verification_tokens (id,user_id,normalized_email,token_hash,failed_attempts,created_at,expires_at) VALUES (${quote(id)},${quote(account.id)},${quote(account.email.toUpperCase())},${quote(hash)},0,now(),now()+interval '10 minutes');`,
  )
  as(name)
  await authApi.confirmCode(code)
  assert.equal((await authApi.me()).emailVerified, true)
}
const filters: ProjectFilters = {
  search: '',
  archived: '',
  leadUserId: '',
  sort: 'name',
  descending: false,
  page: 1,
}
try {
  await check(
    'Vite health proxy reaches the real API and PostgreSQL',
    async () => {
      const response = await nativeFetch(new URL('/health/ready', origin))
      assert.equal(response.status, 200)
      assert.equal(await response.text(), 'Healthy')
    },
  )
  await check('registration validation uses actual backend field errors', () =>
    rejects(
      () =>
        authApi.register({ email: 'bad', password: 'weak', displayName: '' }),
      400,
      ['email', 'password', 'displayName'],
    ),
  )
  await check(
    'four users register with actual JWT/refresh-token response contracts',
    async () => {
      for (const name of ['owner', 'manager', 'viewer', 'invited']) {
        const email = `audit-${name}-${suffix}@example.test`
        const tokens = await authApi.register({
          email,
          password,
          displayName: `Audit ${name}`,
        })
        assert.ok(
          tokens.accessToken &&
            tokens.refreshToken &&
            !Number.isNaN(Date.parse(tokens.accessTokenExpiresAt)),
        )
        useSessionStore.getState().setTokens(tokens)
        const user = await authApi.me()
        assert.equal(user.email, email)
        assert.equal(user.emailVerified, false)
        accounts[name] = { id: user.id, email, tokens }
      }
    },
  )
  await check(
    'invalid login is 401; valid login has the expected token shape',
    async () => {
      await rejects(
        () =>
          authApi.signIn({
            email: accounts.owner.email,
            password: 'WrongPassword1!',
          }),
        401,
      )
      accounts.owner.tokens = await authApi.signIn({
        email: accounts.owner.email,
        password,
      })
      as('owner')
    },
  )
  await check(
    'email request without SMTP is a safe 503, not a false success',
    () => rejects(authApi.requestCode, 503),
  )
  await check(
    'real email confirmation accepts a seeded leading-zero OTP and is single-use',
    async () => {
      await verify('owner')
      await rejects(() => authApi.confirmCode('012345'), 400)
      await authApi.requestCode()
    },
  )
  as('owner')
  const workspace = await workspaceApi.create(`Đội API & Web ${suffix}`)
  const second = await workspaceApi.create(`Other workspace ${suffix}`)
  await check('workspace create/list/get/rename response format', async () => {
    assert.equal(workspace.role, 'Owner')
    assert.ok(
      (await workspaceApi.list()).some((item) => item.id === workspace.id),
    )
    assert.equal((await workspaceApi.get(workspace.id)).name, workspace.name)
    assert.equal(
      (await workspaceApi.rename(workspace.id, 'Renamed API workspace')).name,
      'Renamed API workspace',
    )
    await rejects(() => workspaceApi.rename(workspace.id, ''), 400, ['name'])
  })
  await check(
    'direct member add/list/role change/removal and duplicate rules',
    async () => {
      await workspaceApi.addMember(
        workspace.id,
        accounts.manager.email,
        'Manager',
      )
      await workspaceApi.addMember(
        workspace.id,
        accounts.viewer.email,
        'Viewer',
      )
      assert.equal((await workspaceApi.members(workspace.id)).length, 3)
      await rejects(
        () =>
          workspaceApi.addMember(workspace.id, accounts.viewer.email, 'Viewer'),
        409,
      )
      assert.equal(
        (
          await workspaceApi.changeRole(
            workspace.id,
            accounts.viewer.id,
            'Admin',
          )
        ).role,
        'Admin',
      )
      await workspaceApi.changeRole(workspace.id, accounts.viewer.id, 'Viewer')
      await workspaceApi.removeMember(workspace.id, accounts.viewer.id)
      await workspaceApi.addMember(
        workspace.id,
        accounts.viewer.email,
        'Viewer',
      )
      await rejects(
        () => workspaceApi.removeMember(workspace.id, accounts.owner.id),
        403,
      )
      await rejects(
        () => workspaceApi.addMember(workspace.id, 'bad', 'Bad' as Role),
        400,
        ['email', 'role'],
      )
    },
  )
  const input: ProjectInput = {
    name: 'Đội A&B / Web',
    projectKey: 'AUDIT',
    description: 'Real frontend HTTP client',
    leadUserId: accounts.manager.id,
    startsOn: '2026-10-21',
    targetOn: '2026-11-21',
  }
  let project = await projectApi.create(workspace.id, input)
  await check(
    'project create/get: GUIDs, nullable fields, ISO dates, numeric version',
    async () => {
      assert.equal(project.workspaceId, workspace.id)
      assert.equal(project.startsOn, input.startsOn)
      assert.equal(project.targetOn, input.targetOn)
      assert.equal(project.leadUserId, accounts.manager.id)
      assert.equal(typeof project.version, 'number')
      assert.equal(project.archivedAt, null)
      const loaded = await projectApi.get(workspace.id, project.id)
      const normalize = (value: typeof project) => ({
        ...value,
        createdAt: Date.parse(value.createdAt),
        updatedAt: Date.parse(value.updatedAt),
      })
      assert.deepEqual(normalize(loaded), normalize(project))
      await rejects(() => projectApi.create(workspace.id, input), 409)
      await rejects(
        () =>
          projectApi.create(workspace.id, {
            ...input,
            projectKey: '!',
            startsOn: '2026-11-21',
            targetOn: '2026-10-21',
          }),
        400,
        ['projectKey', 'targetOn'],
      )
    },
  )
  await check(
    'project search, lead/archive filters, all sort fields and pagination',
    async () => {
      for (const sort of ['name', 'key', 'createdAt', 'updatedAt']) {
        const page = await projectApi.list(workspace.id, {
          ...filters,
          search: 'A&B /',
          leadUserId: accounts.manager.id,
          archived: 'false',
          sort,
          descending: true,
        })
        assert.equal(page.total, 1)
        assert.equal(page.page, 1)
        assert.equal(page.pageSize, 20)
        assert.equal(page.items[0].id, project.id)
      }
      assert.equal(
        (await projectApi.list(workspace.id, { ...filters, page: 2 })).items
          .length,
        0,
      )
      await rejects(
        () => projectApi.list(workspace.id, { ...filters, sort: 'unsafe' }),
        400,
        ['sort'],
      )
    },
  )
  await check(
    'project membership pagination and add/remove response contracts',
    async () => {
      const page = await projectApi.members(workspace.id, project.id, 1)
      assert.equal(page.items[0].userId, accounts.manager.id)
      assert.equal(
        (
          await projectApi.addMember(
            workspace.id,
            project.id,
            accounts.viewer.id,
          )
        ).role,
        'Viewer',
      )
      await rejects(
        () =>
          projectApi.addMember(workspace.id, project.id, accounts.viewer.id),
        409,
      )
      await projectApi.removeMember(
        workspace.id,
        project.id,
        accounts.viewer.id,
      )
      await projectApi.addMember(workspace.id, project.id, accounts.viewer.id)
      await rejects(
        () =>
          projectApi.removeMember(
            workspace.id,
            project.id,
            accounts.manager.id,
          ),
        409,
      )
    },
  )
  await check(
    'project updates send the expected version and reject stale writes',
    async () => {
      project = await projectApi.get(workspace.id, project.id)
      const version = project.version
      project = await projectApi.update(
        workspace.id,
        project.id,
        { ...input, description: null, startsOn: null, targetOn: null },
        version,
      )
      assert.equal(project.description, null)
      assert.equal(project.startsOn, null)
      assert.equal(project.version, version + 1)
      await rejects(
        () => projectApi.update(workspace.id, project.id, input, version),
        409,
      )
    },
  )
  await check(
    'Manager manages an assigned project but cannot create projects or manage workspace',
    async () => {
      as('manager')
      assert.equal(
        (await projectApi.get(workspace.id, project.id)).id,
        project.id,
      )
      await rejects(
        () =>
          projectApi.create(workspace.id, { ...input, projectKey: 'DENIED' }),
        403,
      )
      await rejects(() => workspaceApi.rename(workspace.id, 'Denied'), 403)
      await rejects(() => workspaceApi.invitations(workspace.id, 1), 403)
      project = await projectApi.update(
        workspace.id,
        project.id,
        input,
        project.version,
      )
    },
  )
  await check(
    'Viewer can read an assigned project but cannot mutate it',
    async () => {
      as('viewer')
      assert.equal(
        (await projectApi.get(workspace.id, project.id)).id,
        project.id,
      )
      await rejects(
        () =>
          projectApi.update(workspace.id, project.id, input, project.version),
        403,
      )
      await rejects(
        () => projectApi.archive(workspace.id, project.id, true),
        403,
      )
      await rejects(
        () =>
          projectApi.addMember(workspace.id, project.id, accounts.viewer.id),
        403,
      )
    },
  )
  await check('outsider and cross-workspace resources return 404', async () => {
    as('invited')
    await rejects(() => workspaceApi.get(workspace.id), 404)
    await rejects(() => projectApi.get(workspace.id, project.id), 404)
    as('owner')
    await rejects(() => projectApi.get(second.id, project.id), 404)
  })
  await check(
    'archive/restore uses POST, returns Project and enforces read-only state',
    async () => {
      project = await projectApi.archive(workspace.id, project.id, true)
      assert.ok(project.archivedAt)
      assert.equal(
        (await projectApi.list(workspace.id, { ...filters, archived: 'true' }))
          .total,
        1,
      )
      await rejects(
        () =>
          projectApi.update(workspace.id, project.id, input, project.version),
        409,
      )
      await rejects(
        () =>
          projectApi.addMember(workspace.id, project.id, accounts.viewer.id),
        409,
      )
      project = await projectApi.archive(workspace.id, project.id, false)
      assert.equal(project.archivedAt, null)
    },
  )
  const invitation = await workspaceApi.invite(
    workspace.id,
    accounts.invited.email,
    'Developer',
  )
  await check(
    'invitation create/list/get: actual token, fragment URL, uppercase email and paginated DTO',
    async () => {
      assert.ok(invitation.token)
      assert.equal(invitation.invitation.status, 'Pending')
      const link = frontendInvitationLink(origin, {
        id: invitation.invitation.id,
        token: invitation.token,
      })
      assert.deepEqual(
        parseInvitationLink(link),
        parseInvitationLink(invitation.invitationUrl),
      )
      const page = await workspaceApi.invitations(workspace.id, 1)
      assert.equal(page.total, 1)
      assert.equal('token' in page.items[0], false)
      as('invited')
      const detail = await invitationApi.get(invitation.invitation.id)
      assert.equal(detail.email, accounts.invited.email.toUpperCase())
    },
  )
  await check(
    'unverified invitation response is 403; verified acceptance is 204, replay 409',
    async () => {
      await rejects(() => invitationApi.respond(invitation.token, true), 403)
      await verify('invited')
      await invitationApi.respond(invitation.token, true)
      await rejects(() => invitationApi.respond(invitation.token, true), 409)
      assert.equal((await workspaceApi.get(workspace.id)).role, 'Developer')
    },
  )
  await check(
    'invitation rejection and revocation use correct payload and terminal states',
    async () => {
      as('owner')
      const reject = await workspaceApi.invite(
        second.id,
        accounts.invited.email,
        'Viewer',
      )
      as('invited')
      await invitationApi.respond(reject.token, false)
      assert.equal(
        (await invitationApi.get(reject.invitation.id)).status,
        'Revoked',
      )
      as('owner')
      const revoke = await workspaceApi.invite(
        second.id,
        accounts.manager.email,
        'Manager',
      )
      await workspaceApi.revoke(second.id, revoke.invitation.id)
      assert.equal(
        (await workspaceApi.invitations(second.id, 1)).items.find(
          (item) => item.id === revoke.invitation.id,
        )?.status,
        'Revoked',
      )
    },
  )
  await check(
    'ownership transfer rejects unverified targets and changes both roles after success',
    async () => {
      await rejects(
        () => workspaceApi.transfer(workspace.id, accounts.manager.id),
        400,
      )
      await workspaceApi.transfer(workspace.id, accounts.invited.id)
      assert.equal((await workspaceApi.get(workspace.id)).role, 'Admin')
      as('invited')
      assert.equal((await workspaceApi.get(workspace.id)).role, 'Owner')
    },
  )
  await check(
    'automatic refresh rotates through the real /auth/refresh endpoint',
    async () => {
      as('owner')
      const previous = accounts.owner.tokens.refreshToken
      useSessionStore.getState().setTokens({
        ...accounts.owner.tokens,
        accessTokenExpiresAt: '2000-01-01T00:00:00Z',
      })
      await workspaceApi.list()
      save('owner')
      assert.notEqual(accounts.owner.tokens.refreshToken, previous)
    },
  )
  await check(
    'password recovery request handles unconfigured SMTP without inventing success',
    () => rejects(() => authApi.forgotPassword(accounts.viewer.email), 503),
  )
  await check(
    'password-reset OTP/grant/request formats work with seeded test challenge',
    async () => {
      const account = accounts.viewer
      const id = randomUUID()
      const code = '000123'
      const hash = createHmac('sha256', signingKey!)
        .update(
          `flowforge:email-verification:${id.replaceAll('-', '')}:${account.id.replaceAll('-', '')}:password-reset:${code}`,
        )
        .digest('hex')
        .toUpperCase()
      sql(
        `INSERT INTO password_reset_challenges(id,user_id,normalized_email,code_hash,failed_attempts,created_at,expires_at) VALUES (${quote(id)},${quote(account.id)},${quote(account.email.toUpperCase())},${quote(hash)},0,now(),now()+interval '10 minutes');`,
      )
      await rejects(
        () => authApi.verifyPasswordReset(account.email, '999999'),
        400,
      )
      const grant = await authApi.verifyPasswordReset(account.email, code)
      assert.ok(grant.resetToken && Date.parse(grant.expiresAt))
      await rejects(
        () => authApi.resetPassword(grant.resetToken, 'weak'),
        400,
        ['password'],
      )
      await authApi.resetPassword(grant.resetToken, 'NewAuditPassword1!')
      await rejects(
        () => authApi.resetPassword(grant.resetToken, 'NewAuditPassword1!'),
        400,
      )
      await rejects(
        () => authApi.signIn({ email: account.email, password }),
        401,
      )
      assert.ok(
        (
          await authApi.signIn({
            email: account.email,
            password: 'NewAuditPassword1!',
          })
        ).accessToken,
      )
    },
  )
  await check(
    'logout returns 204 and revoked refresh cannot restore the session',
    async () => {
      as('owner')
      const refreshToken = accounts.owner.tokens.refreshToken
      await authApi.signOut(refreshToken)
      useSessionStore.getState().setTokens({
        ...accounts.owner.tokens,
        accessTokenExpiresAt: '2000-01-01T00:00:00Z',
      })
      await rejects(() => workspaceApi.list(), 401)
      assert.equal(useSessionStore.getState().tokens, null)
    },
  )
} finally {
  globalThis.fetch = nativeFetch
  useSessionStore.getState().clear()
  const operations = [
    ...new Set(
      http.map(
        (item) =>
          `${item.method} ${item.path.replace(/[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}/g, ':id')}`,
      ),
    ),
  ]
    .sort()
    .map((value) => {
      const [method, path] = value.split(' ')
      return { method, path }
    })
  const report = {
    date: new Date().toISOString(),
    transport:
      'actual frontend clients → Vite proxy → unmodified ASP.NET Core API → disposable PostgreSQL 17',
    smtp: 'not configured; OTP challenges seeded only in disposable test database',
    totals: {
      passed: checks.filter((item) => item.passed).length,
      failed: checks.filter((item) => !item.passed).length,
      httpRequests: http.length,
      operations: operations.length,
    },
    checks,
    operations,
    http,
  }
  writeFileSync(
    new URL('live-backend.json', output),
    JSON.stringify(report, null, 2) + '\n',
  )
  console.log(JSON.stringify(report.totals))
}
