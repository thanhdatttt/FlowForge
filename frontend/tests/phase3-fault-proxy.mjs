// Test-only loopback proxy. Faults affect reads only; writes still reach the real backend.
// Used to verify drafts survive transient refetch failures without changing backend source.
import { createServer, request } from 'node:http'
if (process.env.FLOWFORGE_PHASE3_DISPOSABLE !== '1')
  throw new Error('Disposable audit opt-in is required.')
const upstream = new URL(
  process.env.FLOWFORGE_AUDIT_UPSTREAM || 'http://127.0.0.1:5193',
)
if (!['127.0.0.1', 'localhost'].includes(upstream.hostname))
  throw new Error('Upstream must be localhost.')
let failReads = false
let failures = 0
createServer(async (req, res) => {
  if (req.url === '/__audit/faults' && req.method === 'POST') {
    let body = ''
    for await (const part of req) {
      body += part
      if (body.length > 256) {
        res.writeHead(413)
        res.end()
        return
      }
    }
    try {
      failReads = JSON.parse(body).failReads === true
    } catch {
      res.writeHead(400)
      res.end()
      return
    }
    res.setHeader('Content-Type', 'application/json')
    res.end(JSON.stringify({ failReads, failures }))
    return
  }
  if (
    failReads &&
    req.method === 'GET' &&
    req.url?.startsWith('/api/v1/workspaces/')
  ) {
    failures++
    res.writeHead(503, { 'Content-Type': 'application/problem+json' })
    res.end(
      JSON.stringify({ title: 'Temporary audit refetch failure', status: 503 }),
    )
    return
  }
  const forwarded = request(
    new URL(req.url || '/', upstream),
    { method: req.method, headers: { ...req.headers, host: upstream.host } },
    (response) => {
      res.writeHead(response.statusCode || 502, response.headers)
      response.pipe(res)
    },
  )
  forwarded.on('error', () => {
    res.writeHead(502)
    res.end()
  })
  req.pipe(forwarded)
}).listen(5195, '127.0.0.1', () =>
  console.log('Disposable fault proxy listening on 127.0.0.1:5195'),
)
