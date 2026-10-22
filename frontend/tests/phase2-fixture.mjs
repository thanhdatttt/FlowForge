// Disposable in-memory browser fixture. No database, SMTP or persistent accounts.
// node tests/phase2-fixture.mjs
// API_URL=http://127.0.0.1:5190 npm run dev -- --host 127.0.0.1 --port 5188
import { createServer } from 'node:http'
import { randomUUID } from 'node:crypto'
const ws = '10000000-0000-0000-0000-000000000001'
const pid = '20000000-0000-0000-0000-000000000001'
const uid = '30000000-0000-0000-0000-000000000001'
let role = 'Owner'
let user = {
  id: uid,
  displayName: 'Alex Morgan',
  email: 'owner@example.test',
  emailVerified: true,
}
const spaces = [{ id: ws, name: 'Studio workspace', role }]
const members = [
  { userId: uid, displayName: 'Alex Morgan', email: user.email, role },
  {
    userId: '30000000-0000-0000-0000-000000000002',
    displayName: 'Sam Chen',
    email: 'sam@example.test',
    role: 'Developer',
  },
  {
    userId: '30000000-0000-0000-0000-000000000003',
    displayName: 'Taylor Lee',
    email: 'taylor@example.test',
    role: 'Manager',
  },
]
const projects = [
  {
    id: pid,
    workspaceId: ws,
    name: 'Website refresh',
    projectKey: 'WEB',
    description: 'A thoughtful home for our next launch.',
    leadUserId: members[2].userId,
    startsOn: '2026-10-20',
    targetOn: '2026-11-20',
    archivedAt: null,
    version: 1,
    createdAt: new Date().toISOString(),
    updatedAt: new Date().toISOString(),
  },
]
const projectMembers = new Map([[pid, [members[2]]]])
const invitations = [
  {
    id: '40000000-0000-0000-0000-000000000001',
    workspaceId: ws,
    email: user.email,
    role: 'Developer',
    status: 'Pending',
    token: 'fixture-token',
    createdAt: new Date().toISOString(),
    expiresAt: new Date(Date.now() + 86400000).toISOString(),
  },
]
const publicInvite = (item) =>
  Object.fromEntries(Object.entries(item).filter(([key]) => key !== 'token'))
const tokens = () => ({
  accessToken: 'fixture-access',
  refreshToken: 'fixture-refresh',
  accessTokenExpiresAt: new Date(Date.now() + 900000).toISOString(),
})
function page(items, url) {
  const number = Number(url.searchParams.get('page') || 1)
  return {
    items: items.slice((number - 1) * 20, number * 20),
    total: items.length,
    page: number,
    pageSize: 20,
  }
}
createServer(async (req, res) => {
  try {
    const url = new URL(req.url, 'http://localhost')
    const parts = url.pathname.split('/').filter(Boolean)
    const method = req.method
    let raw = ''
    for await (const chunk of req) raw += chunk
    const data = raw ? JSON.parse(raw) : {}
    const json = (body, status = 200) => {
      res.writeHead(status, { 'Content-Type': 'application/json' })
      res.end(body === undefined ? undefined : JSON.stringify(body))
    }
    const fail = (status, title) => json({ title }, status)
    if (parts[2] === 'auth') {
      if (parts[3] === 'logout') return json(undefined, 204)
      if (parts[3] === 'login' || parts[3] === 'register') {
        role = data.email.startsWith('viewer')
          ? 'Viewer'
          : data.email.startsWith('manager')
            ? 'Manager'
            : data.email.startsWith('admin')
              ? 'Admin'
              : 'Owner'
        user = {
          ...user,
          email: data.email,
          emailVerified: !data.email.startsWith('unverified'),
        }
        return json(tokens())
      }
      if (parts[3] === 'refresh') return json(tokens())
      if (req.headers.authorization !== 'Bearer fixture-access')
        return fail(401, 'Authentication required.')
      if (parts[3] === 'me') return json(user)
      if (parts[3] === 'email-verification' && parts[4] === 'confirm')
        user.emailVerified = true
      return json(undefined, 204)
    }
    if (req.headers.authorization !== 'Bearer fixture-access')
      return fail(401, 'Authentication required.')
    if (parts[2] === 'invitations') {
      const item = invitations.find((item) =>
        method === 'GET'
          ? item.id === parts[3] && item.email === user.email
          : item.token === data.token && item.email === user.email,
      )
      if (!item) return fail(404, 'Invitation not found.')
      if (method === 'GET') return json(publicInvite(item))
      if (!user.emailVerified) return fail(403, 'Verify your email first.')
      if (item.status !== 'Pending')
        return fail(409, 'The invitation is no longer pending.')
      item.status = parts[3] === 'accept' ? 'Accepted' : 'Revoked'
      return json(undefined, 204)
    }
    if (parts.length === 3) {
      if (method === 'POST') {
        const item = { id: randomUUID(), name: data.name, role: 'Owner' }
        spaces.push(item)
        return json(item, 201)
      }
      return json(spaces.map((space) => ({ ...space, role })))
    }
    const space = spaces.find((space) => space.id === parts[3])
    if (!space) return fail(404, 'Resource not found.')
    const manage = role === 'Owner' || role === 'Admin'
    if (parts.length === 4) {
      if (method === 'PUT') {
        if (!manage) return fail(403, 'Permission denied.')
        space.name = data.name
      }
      return json({ ...space, role })
    }
    if (parts[4] === 'ownership') {
      role = 'Admin'
      return json(undefined, 204)
    }
    if (parts[4] === 'members') {
      if (method === 'GET') return json(members)
      if (!manage) return fail(403, 'Permission denied.')
      if (method === 'POST') {
        if (members.some((member) => member.email === data.email))
          return fail(409, 'The user is already a member.')
        const member = {
          userId: randomUUID(),
          displayName: data.email.split('@')[0],
          ...data,
        }
        members.push(member)
        return json(member, 201)
      }
      const index = members.findIndex((member) => member.userId === parts[5])
      if (index < 0) return fail(404, 'Resource not found.')
      if (method === 'DELETE') {
        members.splice(index, 1)
        return json(undefined, 204)
      }
      members[index].role = data.role
      return json(members[index])
    }
    if (parts[4] === 'invitations') {
      if (!manage) return fail(403, 'Permission denied.')
      if (method === 'GET')
        return json(page(invitations.map(publicInvite), url))
      if (parts[6] === 'revoke') {
        invitations.find((item) => item.id === parts[5]).status = 'Revoked'
        return json(undefined, 204)
      }
      if (
        invitations.some(
          (item) => item.email === data.email && item.status === 'Pending',
        )
      )
        return fail(409, 'A pending invitation already exists.')
      const item = {
        id: randomUUID(),
        workspaceId: space.id,
        ...data,
        status: 'Pending',
        token: randomUUID(),
        createdAt: new Date().toISOString(),
        expiresAt: new Date(Date.now() + 86400000).toISOString(),
      }
      invitations.push(item)
      return json(
        {
          invitation: publicInvite(item),
          token: item.token,
          invitationUrl: `http://127.0.0.1:5190/api/v1/invitations/${item.id}#token=${item.token}`,
        },
        201,
      )
    }
    if (parts[4] === 'projects') {
      if (parts.length === 5 && method === 'GET') {
        let items = projects.filter((item) => item.workspaceId === space.id)
        if (url.searchParams.has('archived'))
          items = items.filter(
            (item) =>
              !!item.archivedAt ===
              (url.searchParams.get('archived') === 'true'),
          )
        if (url.searchParams.get('search'))
          items = items.filter((item) =>
            `${item.name} ${item.projectKey}`
              .toLowerCase()
              .includes(url.searchParams.get('search').toLowerCase()),
          )
        if (url.searchParams.get('leadUserId'))
          items = items.filter(
            (item) => item.leadUserId === url.searchParams.get('leadUserId'),
          )
        const sort = url.searchParams.get('sort') || 'name'
        const key = sort === 'key' ? 'projectKey' : sort
        items.sort(
          (a, b) =>
            String(a[key]).localeCompare(String(b[key])) *
            (url.searchParams.get('descending') === 'true' ? -1 : 1),
        )
        return json(page(items, url))
      }
      if (parts.length === 5 && method === 'POST') {
        if (!manage)
          return fail(403, 'Only Owners and Admins can create projects.')
        if (
          projects.some(
            (item) =>
              item.workspaceId === space.id &&
              item.projectKey === data.projectKey,
          )
        )
          return fail(409, 'Project key already exists.')
        const item = {
          ...data,
          id: randomUUID(),
          workspaceId: space.id,
          archivedAt: null,
          version: 1,
          createdAt: new Date().toISOString(),
          updatedAt: new Date().toISOString(),
        }
        projects.push(item)
        projectMembers.set(
          item.id,
          members.filter((member) => member.userId === data.leadUserId),
        )
        return json(item, 201)
      }
      const item = projects.find(
        (item) => item.id === parts[5] && item.workspaceId === space.id,
      )
      if (!item) return fail(404, 'Resource not found.')
      if (parts.length === 6 && method === 'GET') return json(item)
      if (parts[6] === 'members' && method === 'GET')
        return json(page(projectMembers.get(item.id) || [], url))
      if (!manage && role !== 'Manager') return fail(403, 'Permission denied.')
      if (parts[6] === 'archive' || parts[6] === 'restore') {
        item.archivedAt =
          parts[6] === 'archive' ? new Date().toISOString() : null
        item.version++
        return json(item)
      }
      if (item.archivedAt) return fail(409, 'Archived projects are read-only.')
      if (parts.length === 6 && method === 'PUT') {
        if (item.version !== data.version)
          return fail(409, 'The project changed. Reload and retry.')
        Object.assign(item, data, { version: item.version + 1 })
        return json(item)
      }
      const list = projectMembers.get(item.id) || []
      if (method === 'POST') {
        if (list.some((member) => member.userId === data.userId))
          return fail(409, 'Already a project member.')
        const member = members.find((member) => member.userId === data.userId)
        if (!member) return fail(400, 'Choose an active member.')
        list.push(member)
        projectMembers.set(item.id, list)
        item.version++
        return json(member, 201)
      }
      if (parts[7] === item.leadUserId)
        return fail(409, 'Change or clear the lead first.')
      projectMembers.set(
        item.id,
        list.filter((member) => member.userId !== parts[7]),
      )
      item.version++
      return json(undefined, 204)
    }
    fail(404, 'Fixture route not found.')
  } catch {
    res.writeHead(500)
    res.end('{"title":"Fixture failure"}')
  }
}).listen(5190, '127.0.0.1', () =>
  console.log('Disposable Phase 2 fixture listening on 127.0.0.1:5190'),
)
