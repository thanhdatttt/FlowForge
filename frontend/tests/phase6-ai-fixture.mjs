// Opt-in loopback test double for AI browser interactions. No model, keys or AI writes.
// All other HTTP/SignalR traffic goes to the explicitly disposable real backend.
import { createServer, request } from 'node:http'
import { connect } from 'node:net'
import { randomUUID } from 'node:crypto'

const target = new URL(
  process.env.FLOWFORGE_PHASE6_FIXTURE_API || 'http://127.0.0.1:5206',
)
if (
  process.env.FLOWFORGE_PHASE6_DISPOSABLE !== '1' ||
  target.protocol !== 'http:' ||
  !['127.0.0.1', 'localhost'].includes(target.hostname)
)
  throw Error('Explicit disposable loopback API is required.')
const previews = new Map()
const counts = { propose: 0, accept: 0, summary: 0, receipts: 0 }
let mode = 'success'
const proposals = [
  {
    id: 'foundation',
    title: 'Build onboarding foundation',
    description: 'Create the initial screen.',
    priority: 'High',
    dependsOn: [],
  },
  {
    id: 'wizard',
    title: 'Implement onboarding wizard',
    description: 'Guide the team through setup.',
    priority: 'Medium',
    dependsOn: ['foundation'],
  },
]
const server = createServer(async (req, res) => {
  const json = (body, status = 200, headers = {}) => {
    res.writeHead(status, { 'Content-Type': 'application/json', ...headers })
    res.end(JSON.stringify(body))
  }
  try {
    if (req.url === '/__audit' && req.method === 'GET')
      return json({ mode, counts })
    if (req.url === '/__audit' && req.method === 'POST') {
      let raw = ''
      for await (const chunk of req) raw += chunk
      mode = JSON.parse(raw).mode
      return json({ mode, counts })
    }
    const match =
      /^\/api\/v1\/workspaces\/([^/]+)\/projects\/([^/]+)\/ai\/(tasks\/propose|tasks\/accept|summary)$/.exec(
        req.url,
      )
    if (!match) {
      const proxy = request(
        new URL(req.url, target),
        { method: req.method, headers: { ...req.headers, host: target.host } },
        (upstream) => {
          res.writeHead(upstream.statusCode || 502, upstream.headers)
          upstream.pipe(res)
        },
      )
      proxy.on('error', () => json({ title: 'Test backend unavailable' }, 502))
      req.pipe(proxy)
      return
    }
    const [, workspaceId, projectId, action] = match
    const headers = { Authorization: req.headers.authorization || '' }
    const base = `/api/v1/workspaces/${workspaceId}/projects/${projectId}`
    const access = await fetch(new URL(base, target), { headers })
    if (!access.ok)
      return json({ title: 'Resource unavailable' }, access.status)
    const project = await access.json()
    let raw = ''
    for await (const chunk of req) raw += chunk
    const body = raw ? JSON.parse(raw) : {}
    const workspace = await (
      await fetch(new URL(`/api/v1/workspaces/${workspaceId}`, target), {
        headers,
      })
    ).json()
    if (action.startsWith('tasks/') && workspace.role === 'Viewer')
      return json({ title: 'Permission denied' }, 403)
    if (action.startsWith('tasks/') && project.archivedAt)
      return json({ title: 'Project archived' }, 409)
    if (action === 'tasks/propose') {
      counts.propose++
      if (mode === 'rate-limit')
        return json({ title: 'Fixture throttled' }, 429, { 'Retry-After': '2' })
      if (mode === 'timeout') return json({ title: 'Fixture timeout' }, 504)
      if (mode === 'delayed')
        await new Promise((resolve) => setTimeout(resolve, 2000))
      const previewToken = `fixture-${randomUUID()}`
      previews.set(previewToken, {
        scope: base,
        authorization: headers.Authorization,
      })
      return json({
        previewToken,
        expiresAt: new Date(
          Date.now() + (mode === 'expired' ? -1000 : 900_000),
        ).toISOString(),
        tasks: proposals,
        isAiGenerated: true,
      })
    }
    if (action === 'tasks/accept') {
      counts.accept++
      const preview = previews.get(body.previewToken)
      if (
        !preview ||
        preview.scope !== base ||
        preview.authorization !== headers.Authorization
      )
        return json({ title: 'Unknown test preview' }, 404)
      const identity = JSON.stringify(body.tasks)
      if (preview.receipt && preview.identity !== identity)
        return json({ title: 'Different test selection' }, 409)
      if (!preview.receipt) {
        counts.receipts++
        preview.identity = identity
        preview.receipt = {
          batchId: randomUUID(),
          tasks: body.tasks.map((task, index) => ({
            proposalId: task.id,
            id: randomUUID(),
            key: `FIXTURE-${index + 1}`,
          })),
        }
        if (mode === 'lost-response')
          return json(
            { title: 'Fixture response lost after recording a receipt' },
            503,
          )
      }
      return json(preview.receipt)
    }
    counts.summary++
    const dashboard = await (
      await fetch(new URL(`${base}/dashboard`, target), { headers })
    ).json()
    const facts = {
      totalItems: dashboard.totalItems,
      completedItems: dashboard.completedItems,
      completionPercentage: dashboard.completionPercentage,
      overdueItems: dashboard.overdueItems,
      blockedItems: 0,
    }
    return json({
      projectId,
      asOf: dashboard.asOf,
      todayUtc: dashboard.todayUtc,
      facts,
      status: 'Generated',
      isAiGenerated: true,
      contextItemCount: 1,
      contextTruncated: true,
      narrative: {
        overview: 'Fixture interpretation of project progress.',
        completedWork: 'A foundation task was completed.',
        blockers: 'No detailed blocker evidence in this sample.',
        overdueWork: 'Review the overdue delivery.',
        risks: [
          '<script>window.fixtureExecuted = true</script>',
          'Delivery timing may need review.',
        ],
      },
    })
  } catch {
    json({ title: 'Test fixture failed' }, 502)
  }
})
server.on('upgrade', (req, socket, head) => {
  const upstream = connect(Number(target.port || 80), target.hostname, () => {
    upstream.write(
      `${req.method} ${req.url} HTTP/${req.httpVersion}\r\n${Object.entries({
        ...req.headers,
        host: target.host,
      })
        .map(([key, value]) => `${key}: ${value}`)
        .join('\r\n')}\r\n\r\n`,
    )
    upstream.write(head)
    socket.pipe(upstream).pipe(socket)
  })
  upstream.on('error', () => socket.destroy())
  socket.on('error', () => upstream.destroy())
})
server.listen(5208, '127.0.0.1', () =>
  console.log(
    'Disposable AI browser test double: 127.0.0.1:5208; creates no business data.',
  ),
)
