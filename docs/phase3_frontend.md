# Phase 3 frontend — Work Items and Collaboration

Implemented on 2026-10-21 on top of the existing authentication and Phase 2 UI. Scope follows `feature_specification.md` Phase 3. No backend source, DTO, permission policy, migration, workflow logic, or existing application data was changed.

## Delivered

- Project navigation: Overview, Work items, Labels and Activity; direct work-item routes and browser back/forward. Existing deep-link-through-sign-in behavior is preserved.
- Server-paginated task/issue listing with search, status, priority, type, assignee, reporter, label, inclusive due-date range, archive state, sorting and pagination. Full keys for the current project are normalized to item numbers for the existing search API.
- Creation and snapshot-based editing of all writable work-item fields; assignment/unassignment, statuses loaded from the project API, labels, dates, estimates, archive and restore. Key/reporter/timestamps come from the backend. `isBlocked` and `needsReassignment` are displayed independently of status.
- Project label creation, rename/color edit and deletion; label assignment through the item editor.
- Parent/subtask creation and navigation; dependency addition/removal and blocked state. Known descendants are excluded from parent choices; server validation remains authoritative for all cycles and scope constraints.
- Paginated plain-text comments, native member picker for mentions, readable mention rendering, versioned edits and deletion by author/moderator. The wire format is `@{user-uuid}`. Comment text is rendered as React text, never HTML.
- Paginated attachment metadata, multipart upload using field `file`, authenticated Blob download and uploader/moderator deletion. PNG/JPEG/PDF/TXT, 1 byte–10 MiB; client checks supplement the unchanged server validation.
- Paginated time entries with create/edit/delete, positive minutes, optional notes and local datetime input converted to UTC. No timer or new time-tracking business rules were added.
- Project/item activity with actor, timestamp, action and expandable before/after details. Audit records stay read only.
- Existing palette, Geist typography, shared Button/FormField and Base UI dialogs; responsive layouts, labeled native controls, field errors, loading/empty/error/retry states, and explicit read-only presentation.

## Routes

Prefix: `/workspaces/:workspaceId/projects/:projectId`.

| Suffix | Purpose |
| --- | --- |
| none | Existing project overview/settings/members |
| `/work-items` | Work-item listing |
| `/work-items/:itemId` | Details and collaboration |
| `/labels` | Project labels |
| `/activity` | Project activity |

Production hosting still needs SPA fallback and the documented same-origin `/api` and `/hubs` reverse proxy. Tokens remain in memory; reload requires sign-in.

## Permissions, state and API boundaries

Owners/Admins have implicit project access. Managers/Developers/Viewers need explicit project membership. Developers can write work resources, while Viewers remain read only. Comment/time edit/delete and attachment deletion additionally require authorship/upload ownership or project-management authority. Archived items reject collaboration writes; item restore is available when the project itself is writable. Archived projects block all work-resource writes.

`features/work-items/api.ts` uses the existing versioned REST endpoints. Shared HTTP transport now supports FormData without forcing JSON Content-Type, and Blob responses with the same token refresh/retry behavior as JSON. No new API endpoint is assumed.

TanStack Query owns resource data under `['phase3', workspaceId, projectId, ...]`. Work mutations invalidate this scope and relevant Phase 2 project/member data on success and 403/404/409. Project-member choices use Phase 2-scoped query keys so existing workspace/project membership operations refresh them. Manual Refresh also invalidates Phase 3. Forms keep transient state locally; item/comment editors freeze the resource and its version when opened. Conflict refreshes server data but keeps the draft, disables saving and requires reopening before resubmission. Mutations are not blindly retried on validation, permission or version errors.

Assignee/mention choices combine all pages of explicit project members with workspace Owners/Admins, deduplicated by user ID. Current DTOs do not expose account-active state for implicit workspace candidates; the server validates actual eligibility. Removed/historical users retain their IDs and get a clear unavailable-member fallback rather than being silently displayed as unassigned.

There is no parent filter or children endpoint. Relation views/pickers enumerate all active and archived item pages, at page size 100, then use `parentId`/dependency IDs. This avoids silently dropping children beyond page one. It costs more requests on large projects; dedicated lookup APIs would require a separate backend task. No truncated relation list is presented as complete after a page fails.

Notifications/SignalR, workflow management, dashboards/AI and Kanban drag-and-drop remain later work. Status changes use existing item updates; no client automation engine was introduced. Time entries have no version field in the existing API; the frontend does not claim optimistic concurrency for them.

## Verification — 2026-10-21

Run from `frontend/` with Node 24:

```sh
npm test
npm run lint
npm run typecheck
npm run build
```

- 48 automated tests passed, including all 30 existing auth/Phase 2 tests and 18 Phase 3 tests. Checks cover nested routes, work-resource/moderation boundaries, parent choices, mention rendering, dates, file limits, API contracts, query encoding, multipart refresh/retry, binary downloads, all-page member/relation loading, transient-error draft retention and timestamp precision/DST. The test loader compiles TSX using the existing TypeScript dependency for component rendering checks.
- Lint, TypeScript and production build passed. No dependencies were added.
- Actual frontend clients through Vite → unchanged ASP.NET Core → fresh PostgreSQL: **13 scenarios passed, 107 real HTTP requests**. Covers statuses, labels, task/issue/subtask creation, assignment, filters/sorts, dependencies/cycles, version conflicts, mentions/comment moderation, time entries and UTC normalization, exact file bytes, file isolation, Viewer/outsider restrictions, archives/restores, label-driven version changes, reassignment and activity. Evidence: [live-backend.json](../frontend/test-results/phase3/live-backend.json).
- Browser against that real backend: Developer creation/Owner assignment, labels/dates, mention posting, dependency/blocker display, time entry, concurrent-edit conflict/draft retention, reopening and saving current data, file upload/download, full-key search, archive/read-only/restore, Viewer read-only controls and narrow/mobile layout. Browser console had no warning/error in the checked session. Evidence: [browser checks](../frontend/test-results/phase3/browser-checks.json), [Viewer screenshot](../frontend/test-results/phase3/browser-viewer.jpg).

The full existing backend test suite was rerun during the completion audit: **227 passed, 0 failed, 0 skipped**, including PostgreSQL integration tests. Evidence: [backend-suite.json](../frontend/test-results/phase3/backend-suite.json). SMTP, production hosting and large-project performance were not tested. The disposable backend used for frontend HTTP checks had notification/workflow workers disabled to isolate UI verification; their existing business behavior was not changed.

## Completion audit and regression fixes — 2026-10-21

All twelve frontend tasks were checked against the specification, existing source and REST contracts:

| Task | Status and evidence |
| --- | --- |
| FE3-01 API/types | Complete; actual frontend clients exercise all Phase 3 resource families. |
| FE3-02 Project navigation/deep links | Complete; browser verified direct item and label links through sign-in/reload. |
| FE3-03 List/filter/sort/page | Complete; contract tests and live server-side filter/sort/pagination checks. |
| FE3-04 Item creation/editing | Complete; versioned DTOs, validation and conflict tests; browser draft regression. |
| FE3-05 Details/assignment/archive | Complete; Developer/Viewer/outsider, removed assignee, archive/restore checks. |
| FE3-06 Labels | Complete; live create/update/delete and item-version changes; browser creation and Viewer controls. |
| FE3-07 Subtasks/dependencies | Complete; parent navigation, all-page loading, self/cross-project/cycle rejection. |
| FE3-08 Comments/mentions | Complete; actual mention resolution, edit conflicts, author/moderator boundaries. |
| FE3-09 Attachments | Complete; real multipart upload, byte-exact authorized download, validation and deletion. |
| FE3-10 Time entries | Complete; CRUD/permissions, timezone handling, browser note-only precision check. |
| FE3-11 Activity | Complete; authorized project/item feeds and retained change history. |
| FE3-12 Permissions/quality/docs | Complete; 48 frontend tests, lint/typecheck/build, 227 backend tests and both live audits passed. |

The audit found and fixed three frontend defects:

1. A transient refetch error unmounted an already loaded editor and lost its uncontrolled draft. `QueryState` now keeps the same content mounted when it has successful data and receives a network/429/5xx error, with visible error/retry controls. Workspace/project/item ancestors and Phase 3 resource panels opt in. Initial errors and 401/403/404 still hide content. A new component regression test first reproduced the failure, then passed after the fix. Browser verification injected **13 read-only 503 responses** after a real concurrent-edit 409: title and description remained unchanged and Save stayed disabled; clearing the faults and Refresh recovered current server data.
2. Native `datetime-local` inputs normalize trailing zero seconds/fractions. Comparing their raw text to a formatted timestamp could accidentally replace an unchanged server instant, losing microseconds or choosing the other instant in a repeated DST hour. Time-entry serialization now compares canonical local values and preserves the original timestamp when unchanged. Unit tests cover normalization, microseconds, DST, intentional changes and clearing; a real browser note-only edit preserved exactly `2026-10-21T02:00:00.000123+00:00` on server reread.
3. Full-key search used JavaScript Number conversion, rounding item numbers above the safe-integer range. Leading zeroes are now stripped as text, preserving the backend's long integer key vocabulary; regression cases include `9007199254740993` and all-zero input.

The repeat live checks passed **24 auth/Phase 2 scenarios, 99 requests** and **13 Phase 3 scenarios, 107 requests**. Browser rechecks additionally confirmed label creation, Viewer read-only controls, deep-link sign-in, reload behavior, and no horizontal overflow at a 390px viewport. The final browser session had no console warning/error after recovery and reload. Evidence: [completion audit](../frontend/test-results/phase3/recheck.json), [draft/conflict screenshot](../frontend/test-results/phase3/browser-draft-recovery.jpg), [current Viewer UI](../frontend/test-results/phase3/browser-recheck-viewer.jpg).

For the draft regression, `frontend/tests/phase3-fault-proxy.mjs` is an opt-in loopback-only test proxy, excluded from application code. With the disposable API on port 5193, run `FLOWFORGE_PHASE3_DISPOSABLE=1 node tests/phase3-fault-proxy.mjs` from `frontend/`, then Vite with `API_URL=http://127.0.0.1:5195`. POST `{"failReads":true}` to `http://127.0.0.1:5195/__audit/faults` to fail workspace/project GET requests with 503 while writes still reach the real API. A concurrent API edit followed by saving the already open browser editor produces the version conflict. POST `{"failReads":false}` to restore reads. Stop the proxy after testing. No fault-control route exists in the application/backend.

### Reproduce real-backend verification

Use a fresh disposable database, never the existing application database. From the repo root:

```sh
docker run --rm -d --name flowforge-fe-audit-db-phase3-20261021 \
  -e POSTGRES_PASSWORD=flowforge-disposable-phase3 \
  -p 127.0.0.1:55440:5432 postgres:17-alpine
docker exec flowforge-fe-audit-db-phase3-20261021 pg_isready -U postgres
# Wait until PostgreSQL accepts connections, then:
docker exec flowforge-fe-audit-db-phase3-20261021 createdb -U postgres flowforge_frontend_phase3
```

Start the unmodified API from `backend/`:

```sh
ASPNETCORE_ENVIRONMENT=Development \
ConnectionStrings__FlowForge='Host=127.0.0.1;Port=55440;Database=flowforge_frontend_phase3;Username=postgres;Password=flowforge-disposable-phase3' \
Jwt__SigningKey=frontend-phase3-test-signing-key-20261021 \
Database__AutoMigrate=true Notifications__Enabled=false Workflows__Enabled=false \
DataProtection__KeyPath=/tmp/flowforge-phase3-keys \
Attachments__StoragePath=/tmp/flowforge-phase3-attachments \
dotnet run --project src/FlowForge.Api --urls http://127.0.0.1:5191
```

Start Vite in another terminal from `frontend/`:

```sh
API_URL=http://127.0.0.1:5191 npm run dev -- --host 127.0.0.1 --port 5192
```

Run the actual client audit from `frontend/`:

```sh
FLOWFORGE_LIVE_ORIGIN=http://127.0.0.1:5192 FLOWFORGE_PHASE3_DISPOSABLE=1 npm run test:phase3:live
```

The runner creates five test accounts and uses no direct SQL or email delivery. It writes a report without tokens, plus temporary disposable browser credentials to `/tmp/flowforge-phase3-browser.json`. Registration is limited to five requests/minute; restart the disposable API before immediately repeating the full audit. Stop test API/Vite processes with Ctrl+C, stop the named disposable container, and remove its temporary browser-credential file when done. Existing development configuration and production/local application data remain untouched.
