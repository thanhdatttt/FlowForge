// Actual frontend clients through Vite -> unchanged ASP.NET Core -> disposable PostgreSQL.
// See docs/phase3_frontend.md. No direct SQL, SMTP, or persistent session credentials.
import assert from 'node:assert/strict'
import { randomUUID } from 'node:crypto'
import { mkdirSync, writeFileSync } from 'node:fs'
import { authApi } from '../src/features/auth/api.ts'
import {
  useSessionStore,
  type AuthTokens,
} from '../src/features/auth/session-store.ts'
import {
  projectApi,
  workspaceApi,
  type Role,
} from '../src/features/workspaces/api.ts'
import {
  workApi,
  type Scope,
  type WorkItem,
  type WorkItemInput,
} from '../src/features/work-items/api.ts'
import { itemInput } from '../src/features/work-items/rules.ts'

const origin = process.env.FLOWFORGE_LIVE_ORIGIN
if (
  !origin ||
  process.env.FLOWFORGE_PHASE3_DISPOSABLE !== '1' ||
  !['localhost', '127.0.0.1'].includes(new URL(origin).hostname)
)
  throw new Error(
    'Set a localhost FLOWFORGE_LIVE_ORIGIN and FLOWFORGE_PHASE3_DISPOSABLE=1 for an explicitly disposable test backend.',
  )
const output = new URL('../test-results/phase3/', import.meta.url)
mkdirSync(output, { recursive: true })
const http: { method: string; path: string; status: number }[] = []
const checks: { name: string; passed: boolean; error?: string }[] = []
const nativeFetch = globalThis.fetch
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
async function check(name: string, run: () => Promise<void>) {
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
const accounts: Record<
  string,
  { id: string; email: string; tokens: AuthTokens }
> = {}
const password = 'Phase3-audit-password!2026'
const suffix = randomUUID().slice(0, 8)
const as = (name: string) =>
  useSessionStore.getState().setTokens(accounts[name].tokens)
const rejected = (action: () => Promise<unknown>, status: number) =>
  assert.rejects(action, { status })
let scope: Scope
let first: WorkItem
let child: WorkItem
let prerequisite: WorkItem
let labelId: string
const input = (title: string): WorkItemInput => ({
  title,
  description: 'Phase 3 real backend test',
  type: 'Task',
  status: 'Todo',
  priority: 'Medium',
  assigneeUserId: null,
  parentId: null,
  startDate: null,
  dueDate: null,
  estimateMinutes: null,
  labelIds: [],
})
try {
  await check(
    'Five disposable accounts, workspace and explicit project memberships',
    async () => {
      for (const name of [
        'owner',
        'developer',
        'manager',
        'viewer',
        'outsider',
      ]) {
        const email = `phase3-${name}-${suffix}@example.test`
        const tokens = await authApi.register({
          email,
          password,
          displayName: `Phase 3 ${name}`,
        })
        useSessionStore.getState().setTokens(tokens)
        const user = await authApi.me()
        accounts[name] = { id: user.id, email, tokens }
      }
      as('owner')
      const workspace = await workspaceApi.create(`Phase 3 audit ${suffix}`)
      for (const [name, role] of [
        ['developer', 'Developer'],
        ['manager', 'Manager'],
        ['viewer', 'Viewer'],
        ['outsider', 'Developer'],
      ] as [string, Role][])
        await workspaceApi.addMember(workspace.id, accounts[name].email, role)
      const project = await projectApi.create(workspace.id, {
        name: 'Frontend Phase 3',
        projectKey: 'PTHREE',
        description: 'Disposable audit',
        leadUserId: null,
        startsOn: null,
        targetOn: null,
      })
      scope = { workspaceId: workspace.id, projectId: project.id }
      for (const name of ['developer', 'manager', 'viewer'])
        await projectApi.addMember(workspace.id, project.id, accounts[name].id)
      const members = await workApi.members(scope)
      assert.ok(members.some((m) => m.userId === accounts.owner.id))
      assert.ok(!members.some((m) => m.userId === accounts.outsider.id))
    },
  )
  await check(
    'Statuses and label lifecycle use existing contracts',
    async () => {
      as('developer')
      assert.deepEqual(
        (await workApi.statuses(scope)).map((s) => s.name),
        ['Todo', 'InProgress', 'InReview', 'Done', 'Blocked'],
      )
      const label = await workApi.saveLabel(scope, {
        name: 'Frontend',
        color: '#5146d6',
      })
      labelId = label.id
      assert.equal(
        (await workApi.saveLabel(scope, { name: 'UI', color: null }, labelId))
          .name,
        'UI',
      )
      assert.ok((await workApi.labels(scope)).some((l) => l.id === labelId))
    },
  )
  await check(
    'Developer creates task, issue and subtask; implicit Owner can be assignee',
    async () => {
      first = await workApi.create(scope, {
        ...input('Implement phase three'),
        assigneeUserId: accounts.owner.id,
        labelIds: [labelId],
        startDate: '2026-10-21',
        dueDate: '2026-11-02',
        estimateMinutes: 90,
      })
      prerequisite = await workApi.create(scope, {
        ...input('API prerequisite'),
        type: 'Issue',
        priority: 'Critical',
      })
      child = await workApi.create(scope, {
        ...input('Subtask'),
        parentId: first.id,
        assigneeUserId: accounts.developer.id,
      })
      assert.equal(first.key, 'PTHREE-1')
      assert.equal(first.assigneeUserId, accounts.owner.id)
      assert.equal(child.parentId, first.id)
      assert.ok((await workApi.relations(scope)).some((i) => i.id === child.id))
    },
  )
  await check(
    'Server-side filtering, sorting and pagination return exact results',
    async () => {
      const page = await workApi.list(scope, {
        search: 'Implement',
        status: 'Todo',
        type: 'Task',
        priority: 'Medium',
        assigneeUserId: accounts.owner.id,
        reporterUserId: accounts.developer.id,
        labelId,
        dueAfter: '2026-10-21',
        dueBefore: '2026-11-03',
        sort: 'dueDate',
        descending: false,
        pageSize: 1,
      })
      assert.equal(page.total, 1)
      assert.equal(page.items[0].id, first.id)
      for (const sort of [
        'createdAt',
        'updatedAt',
        'dueDate',
        'priority',
        'key',
      ])
        assert.equal(
          (await workApi.list(scope, { sort, page: 2, pageSize: 1 })).items
            .length,
          1,
        )
    },
  )
  await check(
    'Dependencies increment version, expose blockers and reject self-links/cycles',
    async () => {
      await workApi.addDependency(scope, first.id, prerequisite.id)
      const blocked = await workApi.get(scope, first.id)
      assert.equal(blocked.isBlocked, true)
      assert.ok(blocked.version > first.version)
      await rejected(
        () => workApi.addDependency(scope, first.id, first.id),
        400,
      )
      await rejected(
        () => workApi.addDependency(scope, prerequisite.id, first.id),
        400,
      )
      await rejected(
        () => workApi.update(scope, first.id, itemInput(first), first.version),
        409,
      )
      await workApi.removeDependency(scope, first.id, prerequisite.id)
      first = await workApi.get(scope, first.id)
      assert.equal(first.isBlocked, false)
      await rejected(
        () =>
          workApi.update(
            scope,
            first.id,
            { ...itemInput(first), parentId: child.id },
            first.version,
          ),
        400,
      )
    },
  )
  await check(
    'Work item updates preserve nullable fields and reject stale versions',
    async () => {
      const before = first
      first = await workApi.update(
        scope,
        first.id,
        {
          ...itemInput(first),
          status: 'InProgress',
          priority: 'High',
          assigneeUserId: accounts.developer.id,
          estimateMinutes: 0,
        },
        first.version,
      )
      assert.equal(first.status, 'InProgress')
      assert.equal(first.estimateMinutes, 0)
      await rejected(
        () =>
          workApi.update(scope, first.id, itemInput(before), before.version),
        409,
      )
      await rejected(
        () =>
          workApi.create(scope, {
            ...input('Invalid date'),
            startDate: '2026-11-02',
            dueDate: '2026-10-21',
          }),
        400,
      )
      await rejected(
        () =>
          workApi.create(scope, {
            ...input('Invalid assignee'),
            assigneeUserId: accounts.outsider.id,
          }),
        400,
      )
    },
  )
  await check(
    'Comment mention, edit conflict and author/moderator permissions',
    async () => {
      let comment = await workApi.saveComment(
        scope,
        first.id,
        `Ready for review @{${accounts.owner.id}}`,
      )
      assert.deepEqual(comment.mentionedUserIds, [accounts.owner.id])
      const old = comment
      comment = await workApi.saveComment(
        scope,
        first.id,
        'Edited comment',
        comment,
      )
      await rejected(
        () => workApi.saveComment(scope, first.id, 'Stale', old),
        409,
      )
      await rejected(
        () =>
          workApi.saveComment(
            scope,
            first.id,
            `Invalid @{${accounts.outsider.id}}`,
          ),
        400,
      )
      assert.ok(
        (await workApi.comments(scope, first.id, 1)).items.some(
          (c) => c.id === comment.id,
        ),
      )
      as('owner')
      const ownerComment = await workApi.saveComment(
        scope,
        first.id,
        'Owner review',
      )
      as('developer')
      await rejected(
        () => workApi.deleteComment(scope, first.id, ownerComment.id),
        403,
      )
      await workApi.deleteComment(scope, first.id, comment.id)
      as('manager')
      await workApi.deleteComment(scope, first.id, ownerComment.id)
    },
  )
  await check(
    'Time entries normalize offset timestamps, support edits and enforce authorship',
    async () => {
      as('developer')
      let entry = await workApi.saveTime(scope, first.id, {
        minutes: 45,
        startedAt: '2026-10-21T09:00:00+07:00',
        note: 'Implementation',
      })
      assert.equal(
        Date.parse(entry.startedAt!),
        Date.parse('2026-10-21T02:00:00Z'),
      )
      entry = await workApi.saveTime(
        scope,
        first.id,
        { minutes: 60, startedAt: null, note: null },
        entry.id,
      )
      assert.equal(entry.minutes, 60)
      assert.equal(entry.startedAt, null)
      assert.equal((await workApi.timeEntries(scope, first.id, 1)).total, 1)
      as('owner')
      const ownerEntry = await workApi.saveTime(scope, first.id, {
        minutes: 15,
        startedAt: null,
        note: null,
      })
      as('developer')
      await rejected(
        () => workApi.deleteTime(scope, first.id, ownerEntry.id),
        403,
      )
      await workApi.deleteTime(scope, first.id, entry.id)
      as('manager')
      await workApi.deleteTime(scope, first.id, ownerEntry.id)
    },
  )
  await check(
    'Multipart uploads and authorized binary downloads preserve exact bytes',
    async () => {
      as('developer')
      const file = new File(['Phase three notes\nĐội frontend'], 'notes.txt', {
        type: 'text/plain',
      })
      const attachment = await workApi.upload(scope, first.id, file)
      assert.equal(attachment.sizeBytes, file.size)
      assert.equal(
        await (await workApi.download(scope, first.id, attachment.id)).text(),
        await file.text(),
      )
      assert.equal((await workApi.attachments(scope, first.id, 1)).total, 1)
      await rejected(
        () =>
          workApi.upload(
            scope,
            first.id,
            new File(['fake'], 'fake.pdf', { type: 'application/pdf' }),
          ),
        400,
      )
      as('owner')
      const ownerFile = await workApi.upload(
        scope,
        first.id,
        new File(['owner'], 'review.txt', { type: 'text/plain' }),
      )
      as('developer')
      await rejected(
        () => workApi.deleteAttachment(scope, first.id, ownerFile.id),
        403,
      )
      as('outsider')
      await rejected(
        () => workApi.download(scope, first.id, attachment.id),
        404,
      )
      as('developer')
      await workApi.deleteAttachment(scope, first.id, attachment.id)
      await rejected(
        () => workApi.download(scope, first.id, attachment.id),
        404,
      )
      as('manager')
      await workApi.deleteAttachment(scope, first.id, ownerFile.id)
    },
  )
  await check(
    'Viewer can read collaboration but cannot mutate; project outsider is isolated',
    async () => {
      as('viewer')
      assert.equal((await workApi.get(scope, first.id)).id, first.id)
      await workApi.comments(scope, first.id, 1)
      await workApi.timeEntries(scope, first.id, 1)
      await workApi.attachments(scope, first.id, 1)
      await rejected(() => workApi.create(scope, input('Denied')), 403)
      await rejected(() => workApi.saveComment(scope, first.id, 'Denied'), 403)
      await rejected(() => workApi.archive(scope, first.id, true), 403)
      as('outsider')
      await rejected(() => workApi.list(scope), 404)
      await rejected(() => workApi.activity(scope, 1, first.id), 404)
      as('owner')
      const other = await workspaceApi.create('Other isolated workspace')
      await rejected(
        () => workApi.get({ ...scope, workspaceId: other.id }, first.id),
        404,
      )
    },
  )
  await check(
    'Label deletion removes item labels and increments its version',
    async () => {
      as('developer')
      const before = await workApi.get(scope, first.id)
      await workApi.deleteLabel(scope, labelId)
      first = await workApi.get(scope, first.id)
      assert.ok(!first.labelIds.includes(labelId))
      assert.ok(first.version > before.version)
    },
  )
  await check(
    'Item and project archives enforce read-only, restore preserves work',
    async () => {
      first = await workApi.archive(scope, first.id, true)
      assert.ok(first.archivedAt)
      assert.ok(
        (await workApi.list(scope, { archived: 'true' })).items.some(
          (i) => i.id === first.id,
        ),
      )
      await rejected(
        () => workApi.saveComment(scope, first.id, 'Denied while archived'),
        409,
      )
      await rejected(
        () =>
          workApi.saveTime(scope, first.id, {
            minutes: 1,
            startedAt: null,
            note: null,
          }),
        409,
      )
      first = await workApi.archive(scope, first.id, false)
      as('owner')
      await projectApi.archive(scope.workspaceId, scope.projectId, true)
      await rejected(
        () => workApi.create(scope, input('Denied archived project')),
        409,
      )
      await rejected(() => workApi.archive(scope, first.id, true), 409)
      await projectApi.archive(scope.workspaceId, scope.projectId, false)
    },
  )
  await check(
    'Removed assignee is flagged for reassignment; history and activity survive',
    async () => {
      as('owner')
      await projectApi.removeMember(
        scope.workspaceId,
        scope.projectId,
        accounts.developer.id,
      )
      assert.equal((await workApi.get(scope, first.id)).needsReassignment, true)
      as('developer')
      await rejected(() => workApi.get(scope, first.id), 404)
      as('owner')
      await projectApi.addMember(
        scope.workspaceId,
        scope.projectId,
        accounts.developer.id,
      )
      assert.equal(
        (await workApi.get(scope, first.id)).needsReassignment,
        false,
      )
      const activity = await workApi.activity(scope, 1, first.id)
      assert.ok(activity.total > 0)
      assert.ok(activity.items.some((a) => a.action === 'work-item.restored'))
      assert.ok(activity.items.some((a) => a.action === 'attachment.deleted'))
      assert.ok((await workApi.activity(scope, 1)).total >= activity.total)
    },
  )
  // Prepare real backend data for the manual browser journey; no tokens go into this file.
  as('owner')
  await workApi.saveLabel(scope!, { name: 'Frontend', color: '#5146d6' })
  await workApi.saveComment(
    scope!,
    first!.id,
    `Browser check @{${accounts.developer.id}}`,
  )
  await workApi.upload(
    scope!,
    first!.id,
    new File(['Browser download check'], 'browser-notes.txt', {
      type: 'text/plain',
    }),
  )
  writeFileSync(
    '/tmp/flowforge-phase3-browser.json',
    JSON.stringify(
      {
        scope: scope!,
        itemId: first!.id,
        childId: child!.id,
        prerequisiteId: prerequisite!.id,
        password,
        accounts: Object.fromEntries(
          Object.entries(accounts).map(([name, a]) => [
            name,
            { id: a.id, email: a.email },
          ]),
        ),
      },
      null,
      2,
    ),
  )
} finally {
  globalThis.fetch = nativeFetch
  useSessionStore.getState().clear()
  writeFileSync(
    new URL('live-backend.json', output),
    JSON.stringify(
      {
        executedAt: new Date().toISOString(),
        checks,
        requests: http.length,
        http,
      },
      null,
      2,
    ),
  )
  console.log(
    `${checks.filter((c) => c.passed).length}/${checks.length} scenarios passed; ${http.length} real HTTP requests`,
  )
}
