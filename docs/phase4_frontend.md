# Phase 4 frontend — Notifications and Real-Time

Implemented on 2026-10-21, following `feature_specification.md` Phase 4 and the existing Phase 2/3 frontend. Backend source, DTOs, notification rules, permissions, workers and database migrations are unchanged.

## Delivered

| Task | Implementation |
| --- | --- |
| FE4-01 API/types | Existing notification GET, read and read-all APIs; typed notification and project events; authenticated shared HTTP transport. |
| FE4-02 Notification center | Global Base UI dialog, All/Unread filter, server pagination, timestamps, explicit read state, loading/empty/error/retry states. |
| FE4-03 Read state/badge | Server unread total from `pageSize=1&unreadOnly=true`, individual/all-read mutations, invalidation of every list/count variant, last-page correction. Opening a notification does not implicitly mark it read. |
| FE4-04 Navigation | Work-item routes built from resource IDs; mention notifications link to/focus the comments section. Invitation metadata and original-link input use the existing invitation flow. API `resourceUrl` is never followed as a frontend URL. |
| FE4-05 SignalR lifecycle | One transport per mounted authenticated shell; handlers registered before start; cleanup on session end/unmount, including in-flight startup and React Strict Mode. SignalR is loaded in a separate production chunk. |
| FE4-06 Token refresh | SignalR access-token factory shares the existing HTTP refresh promise and token rotation. Invalid credentials end the session; late refresh cannot restore logout. Query cache is cleared on session loss. |
| FE4-07 Subscriptions | Active project uses JoinProject/LeaveProject; workspace overview uses JoinWorkspace/LeaveWorkspace. Rapid scope changes are serialized and stale-scope events ignored. No subscription group is invented by the client. |
| FE4-08 Live notifications | NotificationReceived contains only an ID; deduplicate then refetch REST list/count. No private message content is synthesized from hub data. |
| FE4-09 Project updates | ProjectUpdated deduplicates eventId and invalidates the affected Phase 3 scope plus Phase 2 workspace queries. Event bursts are coalesced for 150ms. REST remains authoritative. |
| FE4-10 Recovery | Bounded exponential delay of 1–30 seconds retries initial starts, connection loss and failed joins. Join completes before recovery refetch. Online/visible/manual retry rechecks current state. No replay endpoint is assumed. |
| FE4-11 Draft/access behavior | Existing item/comment edit snapshots and versions stay unchanged through background refetch; existing conflict UI retains drafts. REST authorization errors hide inaccessible content. |
| FE4-12 Quality/docs | Automated contract/lifecycle/token tests, live API/PostgreSQL/SignalR audit, browser notification/invitation/draft checks, README updates. |

Notifications are user-wide, including pending invitations before workspace membership. All reads and mutations retain server authorization. Known types are Assigned, Mentioned, Overdue, Invited and Workflow; unknown types have generic display text. No notification title, actor name or comment body is available in the current DTO. The UI uses truthful generic messages and loads resource details only through authorized APIs.

The user chose to retain the existing backend invitation contract: notifications do not contain invitation tokens. Review invitation fetches authorized metadata, explains the requirement, and accepts the original fragment-based invitation link. Acceptance/rejection and email verification remain in the existing Phase 2 flow. No token is manufactured or added to the cache/storage. Backend-generated email links retain their current API path; users can paste them into the frontend.

The controller uses its own serialized retry loop rather than SignalR automatic reconnect, so initial connection and subscription failures are retried as well as dropped transports. Notification list/count also refetch every 60 seconds while their observers are active to reconcile changes such as read state in another tab. Manual Refresh includes Phase 4. Joining/refetching cannot restore permission the server has revoked; failed subscription recovery rechecks REST access. The current hub does not publish workspace membership or project-setting changes as dedicated events, so those are reconciled on reconnect, focus/visibility recovery, manual refresh and normal query reads rather than an invented event contract.

## Verification

From `frontend/` using Node 24:

```sh
npm test
npm run lint
npm run typecheck
npm run build
```

59 automated tests passed, including 11 new phase 4 tests covering exact notification routes/query/204 contracts, navigation, fallback types, shared concurrent refresh, invalid/late refresh, bounded deduplication, subscription ordering, reconnect, initial/permission failures, scope changes and pending-start cleanup. Lint and the production build pass without warnings; TypeScript is checked by the build.

The live audit ran the unchanged ASP.NET Core API and PostgreSQL with notification workers enabled, through the Vite HTTP/WebSocket proxy. **9 scenarios passed, 79 requests**: authorized setup, real SignalR subscription, assignment/mention delivery, read-state/recipient isolation, server token expiry with refresh/rejoin, interrupted transport recovery, invitation metadata/revocation, removed project permission, and overdue delivery. A short-lived JWT is signed only with the disposable server's test key to exercise actual expiry without changing backend code. This never uses a production signing key.

Evidence: [live report](../frontend/test-results/phase4/live-backend.json), [notification UI](../frontend/test-results/phase4/browser-notifications.png), [draft conflict](../frontend/test-results/phase4/browser-draft-conflict.png), [invitation UI](../frontend/test-results/phase4/browser-invitation.png), [390px responsive UI](../frontend/test-results/phase4/browser-mobile.png).

Browser checks use the same real disposable API: sign-in, count/list/read-one/read-all/unread-empty state, notification navigation to comments with focus, dialog dismissal/focus return, remote owner update while an item editor has an unsaved draft, 409 with draft retained and Save disabled, subsequent display of the remote version, logout/account switch without old notifications, invitation metadata and original-link handoff. A browser iframe with a 390px viewport verified the responsive notification dialog and Escape dismissal with focus returned to its trigger. Real SMTP delivery is not exercised (SMTP is unconfigured). A physical mobile-browser/device audit and multi-instance SignalR deployment are outside this local verification.

## Reproduce the live audit

Use a fresh disposable database, never application data. From the repository root:

```sh
docker run --rm -d --name flowforge-fe-phase4-audit-20261021 \
  -e POSTGRES_PASSWORD=phase4-disposable \
  -p 127.0.0.1:55441:5432 postgres:17-alpine
# Wait until pg_isready succeeds.
docker exec flowforge-fe-phase4-audit-20261021 pg_isready -U postgres
docker exec flowforge-fe-phase4-audit-20261021 createdb -U postgres flowforge_frontend_phase4
```

From `backend/`:

```sh
ASPNETCORE_ENVIRONMENT=Development \
ConnectionStrings__FlowForge='Host=127.0.0.1;Port=55441;Database=flowforge_frontend_phase4;Username=postgres;Password=phase4-disposable' \
Jwt__SigningKey=frontend-phase4-test-signing-key-20261021 \
Database__AutoMigrate=true Notifications__Enabled=true \
Notifications__PollSeconds=1 Notifications__OverdueIntervalMinutes=1 \
Workflows__Enabled=false DataProtection__KeyPath=/tmp/flowforge-phase4-keys \
Attachments__StoragePath=/tmp/flowforge-phase4-attachments \
dotnet run --project src/FlowForge.Api --urls http://127.0.0.1:5196
```

From `frontend/`, start Vite:

```sh
API_URL=http://127.0.0.1:5196 npm run dev -- --host 127.0.0.1 --port 5197
```

Then in another frontend terminal:

```sh
FLOWFORGE_LIVE_ORIGIN=http://127.0.0.1:5197 FLOWFORGE_PHASE4_DISPOSABLE=1 \
FLOWFORGE_TEST_SIGNING_KEY=frontend-phase4-test-signing-key-20261021 \
npm run test:phase4:live
```

The runner creates four disposable accounts and waits for real token expiry and the overdue worker (up to roughly two minutes). No direct SQL, SMTP or existing application data is used. It writes the report without credentials and a mode-0600 temporary browser fixture at `/tmp/flowforge-phase4-browser.json`. Restart the disposable API before immediately repeating a full run to reset registration rate limits. Stop the test API/Vite and named container after verification; remove the temporary browser fixture and phase4-only key/storage directories. Production still requires SPA fallback and same-origin `/api` and `/hubs` forwarding, including WebSockets. Existing in-memory sessions still require sign-in after reload.
