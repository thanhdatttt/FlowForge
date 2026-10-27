# Phase 6 frontend — Project dashboard and AI

Implemented on 2026-10-24 against the existing Phase 2–5 React client, [dashboard contracts](dashboard.md) and [AI contracts](ai_backend.md). Backend business logic, endpoints, permissions, database, migrations, workflow engine and provider configuration are unchanged. The existing Overview, work-item and collaboration flows remain available.

## Delivered scope

| Task | Delivered behavior |
| --- | --- |
| FE6-01 API clients/types | Three authenticated dashboard GETs and three AI POSTs use existing contracts and the shared token-refresh lifecycle. Dashboard keys are `['phase6', workspaceId, projectId, ...]`. |
| FE6-02 Navigation | Project tabs add `/dashboard` and `/ai`. Existing Overview remains at the project root. Validated UUID routing, browser history and sign-in deep links continue to work. New screens load lazily. |
| FE6-03 Overview metrics | Server totals, completed items, percentage, overdue count, progress, status and priority breakdowns, zero buckets, snapshot timestamp and UTC date. No client-side recalculation of business metrics. |
| FE6-04 Overdue work | Server-paginated overdue rows, item links, type/status/priority, assignment, due date and optional estimate. The UTC date is part of the list key so midnight refreshes the list. |
| FE6-05 Workload | Server-paginated open counts and estimate totals, Unassigned and retained member attribution. Total means assignee groups. |
| FE6-06 Activity | Ten overview activity records, existing before/after renderer, work-item links, permission-aware workflow execution links and View all activity. Archived-item history remains visible. |
| FE6-07 AI generation | Feature-description form, pending state, cancellation, configuration/unavailable feedback and request-limit cooldown. |
| FE6-08 Preview/editor | Select proposals; edit title, description, priority and dependencies. Preserve proposal IDs; validate selection, missing/self dependencies, cycles, counts and field limits. |
| FE6-09 Explicit acceptance | A review checkbox and explicit Create button submit `accepted: true`. Confirmation disables duplicate submits. After an ambiguous failure the exact token/selection is locked for an explicit retry; successful response shows created item links. |
| FE6-10 Summary | Explicit Generate/Generate again; separate server facts from plain-text AI interpretation, blockers and risks. Show snapshot/context size/truncation and InsufficientData. Changes mark the displayed summary potentially stale without calling AI again. |
| FE6-11 Permissions/errors | Viewers cannot generate/accept tasks; archived projects reject task writes while dashboard/summary remain readable. Server authorization remains authoritative. Field errors and 400/401/403/404/409/410/429/502/503/504 behavior use existing API rules. |
| FE6-12 Synchronization | Work-item/collaboration writes, ProjectUpdated events, reconnect recovery and manual Refresh invalidate dashboard queries. All dashboard reads reconcile every 60 seconds while active; authorization failures stop interval polling. |
| FE6-13 Accessibility/responsive | Existing theme, Button, FormField, TextArea and native checkboxes/selects. Named controls, legends, associated field errors, progress label, explicit confirmation, preview focus and responsive metric/editor layouts. |
| FE6-14 Verification/docs | Frontend regression/Phase 6 tests, backend regression, disposable live HTTP/SignalR checks, browser checks, this document and README updates. |

## State and transport boundaries

Dashboard facts are defined by the backend: each non-archived task/issue/subtask counts once; completion uses status category Done; overdue is unfinished work due strictly before server UTC today. Archived projects remain readable. Workload and overdue pagination have independent state and use page size 20; no unsupported filters or sorts are sent. The separate endpoints can observe different committed snapshots.

AI prompts, previews, tokens, edited selections and summaries stay in component memory. Navigating away or reloading discards them; reload requires signing in again under the existing session model. Preview tokens never enter URLs, local/session storage or TanStack Query caches. Leaving the screen aborts pending AI transport. AI output renders as React text, never HTML. No frontend provider SDK, key entry, model selection or direct Cloud request is added.

The shared API error object gains optional, bounded `retryAfterSeconds` metadata for 429 responses. Existing error messages, authentication, refresh rotation and other request behavior are retained. AI calls supply their own 125-second timeout to accommodate the backend's configurable maximum of 120 seconds and combine it with user cancellation; normal requests retain their original timeout.

Generation and summary never automatically retry on provider/network failures. Confirmation retries require an explicit user action with the frozen payload, allowing the backend's existing idempotent receipt to resolve uncertain writes. A 400 permits correction; 409/410 require resolving/discarding the preview. After expiry, check Work items before starting another breakdown if an earlier confirmation may have committed. The backend assigns accepted tasks Todo/unassigned and may trigger existing workflows after commit.

AI remains optional. An empty project summary can return InsufficientData without a provider; nonempty generation with missing configuration returns 503. Configure the server separately using [ai_backend.md](ai_backend.md). No API key or model was supplied, selected or changed during this work.

## Verification

The full task matrix was rechecked on 2026-10-26; see [Phase 6 completion recheck](phase6_frontend_audit.md) for fresh results and the remaining real-provider verification limits. The evidence below records the original implementation verification.

- `npm run typecheck`, `npm run lint` and `npm run build` passed without warnings. The new screens are separate chunks; the main JS chunk is approximately 489 kB before gzip.
- **83 frontend tests passed**: 71 existing and 12 Phase 6 cases. Coverage includes all six API contracts, authentication, field errors/Retry-After, cancellation, dependency validation, route compatibility, scoped invalidation, exact server metrics, large estimates, retained private-data denial, Viewer/archive controls, summary facts/context, HTML escaping and uncertain confirmation messages.
- **352 backend tests passed, zero failures/skips**, with PostgreSQL; backend build passed with zero warnings/errors. [Backend summary](../frontend/test-results/phase6/backend-suite.json). Raw TRX: `/tmp/flowforge-fe-phase6-results/backend-phase6-frontend.trx`.
- **8 live scenarios and 44 frontend-client requests passed** through Vite to a separate real API/PostgreSQL with workers enabled and no AI model/key. Includes empty metrics/summary, task/issue/subtask counts, 33.33% completion, UTC overdue, Unassigned estimates, 400/403/404/409/503 behavior, pagination, archived-project reads and real SignalR events. [Live report](../frontend/test-results/phase6/live-backend.json).
- Browser checks against the real backend covered Overview compatibility, deep-link reload/sign-in, Viewer task controls, dashboard and unconfigured AI. An externally archived disposable work item updated the already-open dashboard from 4 to 3 items and 25% to 33.33%, including workload, through SignalR without manual refresh.
- The opt-in AI browser test double verified proposal editing, dependency deselection errors, explicit acceptance, a lost confirmation response followed by a successful identical retry (two requests, one fixture receipt), expiry, Retry-After, cancellation with no late preview, summary staleness and plain-text rendering. This double **does not create business data** and is not evidence of real provider connectivity or real acceptance persistence. The unchanged backend test suite independently exercises acceptance persistence/idempotency with its test provider.
- Browser viewport checks at **390 CSS pixels** found `clientWidth = scrollWidth = 375` (15px scrollbar) and no overflowing content in the proposal editor or summary. This is a viewport check, not a physical-device audit.

Evidence: [verification summary](../frontend/test-results/phase6/verification.json), [dashboard](../frontend/test-results/phase6/browser-dashboard.png), [preview](../frontend/test-results/phase6/browser-preview.png), [confirmation retry](../frontend/test-results/phase6/browser-confirmation-retry.png), [summary](../frontend/test-results/phase6/browser-summary.png), [Viewer](../frontend/test-results/phase6/browser-viewer.png), [mobile editor](../frontend/test-results/phase6/browser-mobile-preview.png), [mobile summary](../frontend/test-results/phase6/browser-mobile-summary.png).

Real Ollama Cloud connectivity/model quality, SMTP inbox delivery, deployment and load testing are not claimed. Milestones/documents and advanced reporting remain deferred.

## Reproduce against a disposable backend

Use Node 24, .NET 10, Docker and unused loopback ports. Run from the repository root unless indicated. Never point the runner at existing application data.

```sh
docker run --rm -d --name flowforge-fe-phase6-audit-20261024 \
  -e POSTGRES_PASSWORD=phase6-disposable \
  -p 127.0.0.1:55462:5432 postgres:17-alpine
docker exec flowforge-fe-phase6-audit-20261024 pg_isready -U postgres
dotnet build backend/FlowForge.sln --no-restore -m:1 /p:UseSharedCompilation=false

ConnectionStrings__FlowForge='Host=127.0.0.1;Port=55462;Database=flowforge_frontend_phase6;Username=postgres;Password=phase6-disposable' \
Jwt__SigningKey=phase6-disposable-signing-key-20261024 \
Database__AutoMigrate=true \
DataProtection__KeyPath=/tmp/flowforge-fe-phase6-keys \
Attachments__StoragePath=/tmp/flowforge-fe-phase6-files \
Ollama__ApiKey='' Ollama__Model='' \
Notifications__Enabled=true Workflows__Enabled=true \
dotnet backend/src/FlowForge.Api/bin/Debug/net10.0/FlowForge.Api.dll --urls http://127.0.0.1:5206
```

In another terminal:

```sh
cd frontend
API_URL=http://127.0.0.1:5206 npm run dev -- --host 127.0.0.1 --port 5207
```

In another terminal, from `frontend/`:

```sh
FLOWFORGE_LIVE_ORIGIN=http://127.0.0.1:5207 FLOWFORGE_PHASE6_DISPOSABLE=1 npm run test:phase6:live
```

The runner creates four test accounts and disposable project data, writes a sanitized report, and writes temporary browser credentials to `/tmp/flowforge-phase6-browser.json` with mode 0600. Registration/generation rate limits apply; wait for the window to reset or restart the disposable API before an immediate rerun. The runner assumes AI is unconfigured.

Backend regression, from the repository root:

```sh
FLOWFORGE_TEST_CONNECTION='Host=127.0.0.1;Port=55462;Database=postgres;Username=postgres;Password=phase6-disposable' \
dotnet test backend/FlowForge.sln --no-build -m:1 \
  --logger 'trx;LogFileName=backend-phase6-frontend.trx' \
  --results-directory /tmp/flowforge-fe-phase6-results
```

Optional AI interaction fixture, from `frontend/`, with the disposable API still running:

```sh
FLOWFORGE_PHASE6_DISPOSABLE=1 node tests/phase6-ai-fixture.mjs
# Another terminal:
API_URL=http://127.0.0.1:5208 npm run dev -- --host 127.0.0.1 --port 5209
```

Sign into the fixture frontend with a disposable account from the runner. The fixture forwards normal API/SignalR traffic to localhost:5206, but substitutes AI responses and fake creation receipts. Fixture task links are placeholders. Its loopback-only `POST /__audit` accepts `{ "mode": "success" }`, `lost-response`, `expired`, `rate-limit`, `delayed` or `timeout`; `GET /__audit` exposes request/receipt counters. This route and fixture are outside application source and production builds.

Stop the test API/Vite/fixture processes, then `docker stop flowforge-fe-phase6-audit-20261024`. Remove temporary browser credentials and the Phase 6-only keys/files after testing. Existing application services, configuration and database volumes are untouched.
