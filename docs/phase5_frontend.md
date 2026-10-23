# Phase 5 frontend — Workflow automation

Implemented on 2026-10-23 against the existing Phase 2–4 client and `workflow_engine.md`. The workflow execution engine, permission policies, event scheduling, conditions, actions, retries, email delivery, database and migrations are unchanged. The user approved one additive, read-only backend endpoint for Activity navigation.

## Delivered

| Task | Implementation |
| --- | --- |
| FE5-01 API/types | All nine existing operations plus execution lookup; shared authenticated transport, typed definitions and results, exact version and pagination bodies. |
| FE5-02 Routing/access | Workspace Workflows navigation, project Workflows tab, workflow detail, nested execution and Activity execution deep links. Developer/Viewer views are blocked; server reads/mutations remain authoritative for scoped Managers and revoked access. |
| FE5-03 List | Server pagination and project filter; unfiltered list means all manageable rules including workspace rules. Enabled state, trigger, scope and version; loading, empty, error and retry states. |
| FE5-04 Create/edit | Named structured rule, immutable scope/creator after creation, disabled creation, configuration versions, preserved enabled state. Archived projects block creation/edit/enable but allow disable. |
| FE5-05 Triggers | All seven existing trigger values; UTC semantics explained for date triggers. No client scheduling or event synthesis. |
| FE5-06 Conditions | Zero to 20 ALL conditions, typed field/operator/value controls, explicit multiple values for In, empty operators serialize an empty array. Priority/date comparisons only; condition reordering. Text is not split on commas, pipes or newlines. |
| FE5-07 Actions | One to 20 ordered actions covering all nine types. Stable UUIDs survive reorder; changing type clears irrelevant parameters. Project members/statuses/labels use authorized existing APIs; workspace status/label actions use exact names. Comment mentions have the existing member-picker convention. |
| FE5-08 Validation/conflicts | Explicit server validate action, indexed field errors and error summary; PUT/enable/disable send version. Editor snapshot survives background refetch and 409; stale saves are blocked until explicit draft discard/load. |
| FE5-09 State | Enable/disable dialogs explain eligibility windows, cancelled unfinished actions and preserved committed effects/email jobs. Dialog state pins the reviewed version; conflicts require reopening. Focus returns to the trigger. |
| FE5-10 History/details | Server pagination; immutable definition/version, condition outcome, event/actor, scope, correlation/origin/depth, attempts, timestamps, ordered action diagnostics and independent email delivery state. Nonmatching Succeeded is explicitly shown as actions skipped. |
| FE5-11 Realtime/activity | Existing ProjectUpdated events invalidate Phase 5 queries with the Phase 4 deduplication/coalescing/reconnect lifecycle. Manual refresh includes Phase 5. History/detail also reconcile every 15/5 seconds and current workflow every 30 seconds while active; authorization errors stop interval polling. Activity links use authorized execution lookup; existing Workflow notifications stay in the notification center. |
| FE5-12 Quality/docs | Automated contracts/permissions/editor/view checks, real API/PostgreSQL/SignalR journeys, desktop and 390px browser checks, README and contract updates. |

## Additive API

`GET /api/v1/workspaces/{workspaceId}/workflows/executions/{executionId}` returns the existing `WorkflowExecutionDto`. The execution must belong to the requested workspace and its workflow must pass the same `WorkflowAccess.CanManageAsync` check used by nested details. No new execution authority is introduced. Anonymous requests return 401; inaccessible, absent and wrong-workspace executions return 404.

UI routes are `/workspaces/{workspaceId}/workflows`, `/workspaces/{workspaceId}/workflows/{workflowId}`, `/workspaces/{workspaceId}/projects/{projectId}/workflows`, and `/workspaces/{workspaceId}/workflow-executions/{executionId}`. The nested `/workspaces/{workspaceId}/workflows/{workflowId}/executions/{executionId}` route is also supported. Reload keeps the destination but requires sign-in, following the existing in-memory session contract.

## Verification

- **71 frontend tests passed** (59 existing, 10 Phase 5 tests and 2 Activity-comparison regression tests). Coverage includes exact endpoints and DTOs, server validation/conflicts without write retries, routes, workspace/project permissions, operator arity, stable action IDs and serialization, immutable execution views, HTML escaping, mention controls and archived/unassigned-Manager editor states. [Latest JUnit](../frontend/test-results/phase5/ui-polish/frontend-tests.xml).
- **352 backend tests passed, zero failures/skips**, including the new execution-lookup test for authorization, cross-workspace isolation, assigned Manager access, workspace-rule denial and revoked membership. [Summary](../frontend/test-results/phase5/backend-suite.json). Raw TRX: `/tmp/flowforge-fe-phase5-test-results/backend-phase5-frontend.trx`.
- Frontend lint, TypeScript/production build pass. No dependency was added.
- **10 live scenarios passed, 84 client requests** using real frontend clients through Vite and a separate PostgreSQL/API, with workers enabled. Real SignalR delivered WorkflowExecutionUpdated; all nine actions were exercised (eight successful ordered actions and the expected permanent SendEmail failure with unconfigured SMTP). Checks include scoped Manager permissions, lookup denials, old/new pinned versions, stale writes, skipped conditions, activity and notifications, and disable behavior. [Live report](../frontend/test-results/phase5/live-backend.json).

Browser checks used only disposable data: sign-in, create/configure/validate a disabled rule, reorder actions, enable, generate a real item event, observe its execution in history, inspect pinned details, follow work-item Activity back to execution, retain an unsaved draft through a remote version update, and disable. The updated dialog returns focus to its trigger after mutation. Signing out and following an execution deep link as Developer displays no workflow content or workflow navigation. A 390px iframe verified responsive builder controls and Escape dismissal/focus return. Backend stop/restart also exercised reconnect/refetch recovery.

Evidence: [builder](../frontend/test-results/phase5/browser-builder.png), [execution](../frontend/test-results/phase5/browser-execution.png), [draft protection](../frontend/test-results/phase5/browser-draft-conflict.png), [access denial](../frontend/test-results/phase5/browser-access-denied.png), [390px builder](../frontend/test-results/phase5/browser-mobile.png). This is a browser viewport check, not a physical mobile-device audit. SMTP inbox delivery, production deployment and load testing are not claimed.

OR conditions, canvas drag-and-drop, scripts, workflow deletion, manual execution and manual retries are not offered by this slice. Project settings/membership and workflow configuration changes do not have a dedicated configuration event in the existing hub; normal query reads, focus/reconnect/manual refresh and the active workflow interval reconcile those changes. Workspace status/label validation may find a target in one project without guaranteeing it exists in every execution project; the UI explains this existing backend rule.

## UI polish follow-up (2026-10-23)

Project membership actions align with their heading. Workflow validation success uses a green bordered notice and errors retain red bordered notices. The workflow summary shows only its updated timestamp; trigger, conditions and actions have distinct headings, counts and numbered rows. Enable is green, Disable amber, Delete comment red; state-confirmation buttons are centered. Comment submission is separated from the thread. Notification filters/actions share the same baseline and control height.

Activity changes now use one Field/Before/After table so asymmetric payloads remain aligned. Missing values are explicitly “Not recorded”, distinct from null (“Not set”); no historical value is inferred or rewritten. Regression tests cover asymmetric snapshots, false/zero/empty values, nested JSON and HTML escaping.

The local API at port 8080 was observed to lack `/workflows/executions/{executionId}` in OpenAPI while the endpoint already existed in source. The Activity link was verified against the current API in an isolated PostgreSQL environment. The user authorized rebuilding/restarting only Compose service `api`, preserving the existing database and volumes. The updated local API passed readiness (200), included the lookup in OpenAPI, and returned 401 for anonymous lookup. [Runtime verification](../frontend/test-results/phase5/ui-polish/local-api-update.json). No compatibility scan or workflow-engine change was introduced.

[UI verification](../frontend/test-results/phase5/ui-polish/verification.json), [workflow](../frontend/test-results/phase5/ui-polish/workflow.png), [confirmation](../frontend/test-results/phase5/ui-polish/enable-confirm.png), [notifications](../frontend/test-results/phase5/ui-polish/notifications.png), [comments](../frontend/test-results/phase5/ui-polish/comments.png), [Activity comparison](../frontend/test-results/phase5/ui-polish/activity-comparison.png).

## Reproduce

From the repository root, create a disposable database:

```sh
docker run --rm -d --name flowforge-fe-phase5-audit-20261023 \
  -e POSTGRES_PASSWORD=phase5-disposable \
  -p 127.0.0.1:55451:5432 postgres:17-alpine
docker exec flowforge-fe-phase5-audit-20261023 pg_isready -U postgres
docker exec flowforge-fe-phase5-audit-20261023 createdb -U postgres flowforge_frontend_phase5
```

From `backend/`, run the API with SMTP unconfigured:

```sh
ASPNETCORE_ENVIRONMENT=Development \
ConnectionStrings__FlowForge='Host=127.0.0.1;Port=55451;Database=flowforge_frontend_phase5;Username=postgres;Password=phase5-disposable' \
Jwt__SigningKey=frontend-phase5-test-signing-key-20261023 \
Database__AutoMigrate=true Notifications__Enabled=true Notifications__PollSeconds=1 \
Workflows__Enabled=true Workflows__PollSeconds=1 Workflows__DateScanIntervalMinutes=1 \
DataProtection__KeyPath=/tmp/flowforge-phase5-keys \
Attachments__StoragePath=/tmp/flowforge-phase5-attachments \
Logging__LogLevel__Default=Warning \
dotnet run --project src/FlowForge.Api --urls http://127.0.0.1:5198
```

From `frontend/`, start Vite, then run the live client in another terminal:

```sh
API_URL=http://127.0.0.1:5198 npm run dev -- --host 127.0.0.1 --port 5199
```

```sh
FLOWFORGE_LIVE_ORIGIN=http://127.0.0.1:5199 FLOWFORGE_PHASE5_DISPOSABLE=1 \
npm run test:phase5:live
```

Normal checks from `frontend/`:

```sh
npm test
npm run lint
npm run typecheck
npm run build
```

Full backend regression from `backend/`:

```sh
FLOWFORGE_TEST_CONNECTION='Host=127.0.0.1;Port=55451;Database=postgres;Username=postgres;Password=phase5-disposable' \
dotnet test FlowForge.sln
```

The live runner writes a sanitized report and a mode-0600 temporary fixture at `/tmp/flowforge-phase5-browser.json`. Stop the test API/Vite and named container after checking, then remove that fixture and phase5-only key/storage directories. Restart a fresh disposable API before immediately repeating account registration to reset its rate limiter. Do not run against application data.
