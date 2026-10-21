# Phase 2 frontend — Workspace and Project

Implemented on 2026-10-20. Scope follows Phase 2 in `feature_specification.md`, not the broader phase grouping in `proposal.md`. Backend logic, endpoints, DTOs, migrations and permissions are unchanged.

## Delivered

- Authenticated application shell with workspace switching, browser back/forward, direct project routes, account/email verification and sign-out.
- Workspace listing, creation, detail/settings and renaming.
- Workspace member listing, direct addition of existing accounts, role changes and removal. Owner cannot be demoted or removed using member controls; Admin cannot manage Owner/Admin members.
- Invitation creation, one-time link presentation/copy, paginated status list, revoke, metadata preview, acceptance and rejection.
- Owner-only ownership transfer with explicit acknowledgment. The existing members API does not expose email verification, so the UI explains eligibility and uses the server's validation rather than guessing it.
- Project list with server-side search, archive and lead filters, sorting and pagination.
- Project creation/editing with key, description, lead and dates; project overview with current settings.
- Project member pagination/addition/removal, including the lead-removal restriction.
- Archive/restore and read-only UI for archived projects.
- Query keys scoped by workspace/project; cache refresh after writes and permission/conflict responses; loading, empty, validation and retry states; responsive layouts and accessible Base UI dialogs.

The project editor freezes its fields and `version` together at opening. Background refetch cannot silently move an old form onto a new version. A version conflict refreshes the resource and requires reopening the editor. Other conflicts, such as duplicate/immutable keys, retain the form so users can correct them.

Owners/Admins create projects. Managers manage projects returned by the authorized project API; backend visibility already requires their explicit membership. Developers and Viewers have no project settings/member management controls. Frontend controls do not replace server authorization.

## Routes and invitation decision

| Route | Purpose |
| --- | --- |
| `/workspaces` | Workspace list, creation and invitation link input |
| `/workspaces/:id/projects` | Project list |
| `/workspaces/:id/projects/:projectId` | Project overview and members |
| `/workspaces/:id/members` | Workspace members |
| `/workspaces/:id/invitations` | Manage invitations |
| `/workspaces/:id/settings` | Workspace name and ownership |
| `/invitations/:id#token=...` | Capture an invitation and continue through sign-in/registration |

The user approved frontend links plus an input accepting both frontend and existing API invitation links. A newly created invitation uses the returned invitation ID and token to produce a same-origin frontend link. The secret is only in its fragment; the receiver immediately removes that fragment from the active history entry and keeps the token in memory through authentication and email verification. It never enters browser storage, query keys or the query cache. Reloading loses the pending token: reopen/paste the original invitation link.

Backend-generated email links still point to `/api/v1/invitations/:id#token=...`. Copy/paste those into the workspace home invitation input. Making backend email links open the frontend automatically would require a separate backend/configuration change, outside this task. Production hosting must serve `index.html` for frontend routes and preserve `/api` and `/hubs` forwarding.

Existing in-memory authentication is preserved: reloading requires signing in again. Deep workspace/project links retain their destination through sign-in. Dashboard metrics, task boards, comments, notifications/SignalR and workflow management remain in their respective later frontend phases.

## Verification

Use Node 24 from `frontend/`:

```sh
npm test
npm run lint
npm run typecheck
npm run build
```

All 30 automated tests pass; lint and typecheck complete without warnings, and the production build succeeds. The tests cover existing authentication and Phase 2 role boundaries, all 23 workspace/invitation/project read/write operation contracts, query encoding/pagination, invitation link parsing and rejection of unsafe/missing tokens, nested routes, and propagation of 403/404/409 without retrying writes. Project listing is verified separately with filters.

Browser checks use the disposable in-memory fixture below. It sends no email, writes no database and is not an authorization reference implementation. It exists only for reproducible UI checks.

```sh
# Terminal 1
node tests/phase2-fixture.mjs
# Terminal 2
API_URL=http://127.0.0.1:5190 npm run dev -- --host 127.0.0.1 --port 5188
```

Open `http://127.0.0.1:5188`. Use `owner@example.test`, `admin@example.test`, `manager@example.test` or `viewer@example.test` with any nonempty password. `unverified@example.test` exercises the email-verification state. Restart the fixture to reset its data; stop both processes with Ctrl+C. Never point the fixture at a database or use real credentials.

Verified browser journeys include sign-in, workspace navigation, project creation, project member addition, archive/read-only behavior, workspace role change, invitation creation and API-link input, invitation acceptance/rejection, direct invitation handoff through sign-in with fragment removal, version conflict after a concurrent HTTP edit, sign-out and Viewer controls. Mobile viewport checking found no horizontal overflow; the browser console had no warnings/errors in the final checked session.

These checks validate the frontend against a fixture and the checked-in API contracts. A real backend/database/SMTP end-to-end run has not been performed for this change: the local API was unavailable during this session. The fixture does not establish real permission isolation or SMTP delivery; those remain integration verification tasks when the backend environment is available.

## Real backend verification — 2026-10-21

This audit supersedes the earlier fixture-only integration limitation above. No backend source, behavior, API DTO, migration or production/local data was changed.

| Verification | Result |
| --- | --- |
| Frontend automated suite | 30 passed, 0 failed |
| Lint, TypeScript, production build | Passed; no lint warnings |
| Full backend suite, including PostgreSQL integration tests | 220 passed, 0 failed, 0 skipped |
| Actual frontend HTTP clients through Vite proxy to real ASP.NET Core/PostgreSQL | 24 scenarios passed, 99 API requests, 34 distinct method/route operations |
| Browser using the real backend | Sign-in, workspace listing/creation, project creation with ISO dates and nullable lead, project editing and reread passed; no browser console warnings/errors in the checked session |

Evidence: [live backend request/scenario report](../frontend/test-results/phase2/live-backend.json), [all backend test outcomes](../frontend/test-results/phase2/backend-suite.json), [browser screenshot](../frontend/test-results/phase2/browser-real-backend.png). Raw backend TRX during this run: `/tmp/flowforge-fe-audit-results/backend-full.trx`.

### Contract and integration findings

All 34 existing frontend operations were exercised against the real backend: 10 authentication/email-verification/password-reset operations, 12 workspace/member/ownership/invitation-management operations, 3 invitation-recipient operations and 9 project/member operations. Request methods, route parameters, JSON camelCase property names, Bearer authentication, bodyless POST/DELETE operations, query names, pagination wrappers, role/status strings, nullable GUID/date fields and numeric project versions match the checked-in backend.

Positive and negative checks cover 400 field errors, 401 credentials/revoked refresh, 403 permissions/unverified invitations, 404 resource isolation, 409 duplicates/version conflicts/archive restrictions, 503 missing SMTP, and 204 empty responses. Search containing Unicode, ampersand and slash passes through the proxy correctly. All four project sort fields work. Manager and Viewer restrictions and cross-workspace isolation were tested against the server. Invitation acceptance, rejection and revocation, ownership transfer, refresh rotation, and password reset/replay protection pass.

The initial audit assertion compared POST and GET timestamp strings verbatim. PostgreSQL rounds sub-microsecond precision, so that assertion was corrected to compare parsed instants while retaining exact checks for IDs, dates, nullable fields and versions. This was a test assertion issue; no frontend/backend format change was needed.

SMTP was intentionally unconfigured: verification/recovery request failures correctly produce 503. For successful OTP confirmation and password reset, the runner seeds HMAC-bound challenges **only in the disposable test database**, then calls the actual frontend client and backend endpoints. This verifies the request/response and confirmation/reset logic, not delivery to a real Gmail inbox. Backend integration tests separately use their existing `TestEmailSender`. Actual SMTP delivery and production reverse-proxy hosting are outside this local audit.

### Reproducing the real HTTP audit

Use a fresh disposable PostgreSQL container/database; never use the application's existing database. The runner checks that the origin is localhost and requires explicit audit-container/database names. Start a fresh backend process for each complete run to avoid sharing its registration/login rate-limit budget with earlier runs. No Gmail configuration is needed or used.

One-time test server setup from the repo root (ports used here: PostgreSQL `55439`, backend `5189`, frontend `5188`):

```sh
docker run --rm -d --name flowforge-fe-audit-db-20261021 \
  -e POSTGRES_PASSWORD=flowforge-disposable-audit \
  -p 127.0.0.1:55439:5432 postgres:17-alpine
# Wait for pg_isready to report accepting connections.
docker exec flowforge-fe-audit-db-20261021 pg_isready -U postgres
docker exec flowforge-fe-audit-db-20261021 createdb -U postgres flowforge_frontend_live
```

Start the unmodified backend from `backend/`:

```sh
ASPNETCORE_ENVIRONMENT=Development \
ConnectionStrings__FlowForge='Host=127.0.0.1;Port=55439;Database=flowforge_frontend_live;Username=postgres;Password=flowforge-disposable-audit' \
Jwt__SigningKey=frontend-live-audit-test-signing-key-20261021 \
Database__AutoMigrate=true Notifications__Enabled=false Workflows__Enabled=false \
DataProtection__KeyPath=/tmp/flowforge-fe-audit-keys \
Attachments__StoragePath=/tmp/flowforge-fe-audit-attachments \
dotnet run --project src/FlowForge.Api --urls http://127.0.0.1:5189
```

Start Vite from `frontend/`:

```sh
API_URL=http://127.0.0.1:5189 npm run dev -- --host 127.0.0.1 --port 5188
```

Run the live client audit from another terminal in `frontend/`:

```sh
FLOWFORGE_LIVE_ORIGIN=http://127.0.0.1:5188 \
FLOWFORGE_TEST_DB_CONTAINER=flowforge-fe-audit-db-20261021 \
FLOWFORGE_TEST_DB_NAME=flowforge_frontend_live \
FLOWFORGE_TEST_SIGNING_KEY=frontend-live-audit-test-signing-key-20261021 \
npm run test:live
```

Run the full backend suite from `backend/` against the same disposable PostgreSQL server; its fixtures create and remove their own independent databases:

```sh
FLOWFORGE_TEST_CONNECTION='Host=127.0.0.1;Port=55439;Database=postgres;Username=postgres;Password=flowforge-disposable-audit' \
dotnet test FlowForge.sln -m:1 /p:UseSharedCompilation=false
```

Stop the test backend/Vite processes with Ctrl+C and stop `flowforge-fe-audit-db-20261021` to remove its disposable data. The audit session cleaned up its own test processes and container after verification. Default development configuration remains `/api` → `http://localhost:8080`, matching the backend Compose default; no environment files were changed. Production still needs the documented same-origin `/api` and `/hubs` reverse proxy.
