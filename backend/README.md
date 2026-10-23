# FlowForge backend

This is the .NET 10 modular-monolith backend. It includes authentication, Gmail SMTP email verification, workspaces, invitations, ownership transfer, projects, work items and collaboration, Phase 4 notifications and SignalR, Phase 5 workflow automation, Phase 6 basic project dashboards and provider-independent AI APIs (no model configured), OpenAPI, and health checks.

## Projects

- `src/FlowForge.Api`: HTTP host, module endpoints, and client-facing request/response DTOs in `Contracts/`.
- `src/FlowForge.Application`: use cases and module contracts.
- `src/FlowForge.Domain`: business rules and domain models.
- `src/FlowForge.Infrastructure`: EF Core context and PostgreSQL provider; private files, SMTP delivery, and notification background jobs.
- `tests/FlowForge.Api.Tests`: unit, API smoke, and PostgreSQL integration tests.

The references point inward: Application depends on Domain, Infrastructure depends on Application, and API composes Application and Infrastructure. Add feature code by business module within these projects, following [the architecture document](../docs/architecture.md). Do not create cross-module circular dependencies.

## Run with Docker Compose

Prerequisite: Docker with the Compose plugin. From `backend/`:

```sh
test -f .env || cp .env.example .env
# Edit .env and set POSTGRES_PASSWORD and JWT_SIGNING_KEY.
# Generate a signing key with: openssl rand -base64 48
docker compose up --build -d
docker compose ps
curl http://localhost:8080/health/live
curl http://localhost:8080/health/ready
curl http://localhost:8080/openapi/v1.json
```

Compose binds the API and PostgreSQL to localhost only. Change `API_PORT` or `POSTGRES_PORT` in `.env` if those ports are occupied. PostgreSQL data is kept in the `postgres_data` volume. Stop the stack with `docker compose down`; use `docker compose down -v` only when you intend to delete local database data.

`/health/live` checks the host. `/health/ready` checks that PostgreSQL accepts a connection. OpenAPI JSON is available only in Development. Compose applies EF Core migrations when the API starts.

## Run the API on the host

Prerequisite: the .NET 10 SDK. Start PostgreSQL with `docker compose up -d db`, then set the connection string to match the password and host port in `.env`. For example, with the default `POSTGRES_PORT=5432`:

```sh
export ConnectionStrings__FlowForge='Host=localhost;Port=5432;Database=flowforge;Username=flowforge;Password=your-local-password'
export Jwt__SigningKey="$(openssl rand -base64 48)"
dotnet ef database update --project src/FlowForge.Infrastructure --startup-project src/FlowForge.Api
dotnet run --project src/FlowForge.Api --urls http://localhost:8080
```

Replace `your-local-password` with the value in `.env`. If `API_PORT=8080` is already in use by the Compose API container, stop that container with `docker compose stop api` first or choose a different URL for `dotnet run`. The connection string and a signing key of at least 32 bytes are required at startup. Host runs use the explicit migration command above. Keep the signing key stable across restarts so existing access tokens remain valid.

## Authentication API

Endpoints under `/api/v1/auth`:

| Method | Path | Request | Result |
| --- | --- | --- | --- |
| POST | `/register` | `email`, `password`, `displayName` | 201 and tokens |
| POST | `/login` | `email`, `password` | 200 and tokens |
| POST | `/refresh` | `refreshToken` | 200 and rotated tokens |
| POST | `/logout` | `refreshToken` | 204; revokes that session family |
| GET | `/me` | `Authorization: Bearer <accessToken>` | Current user, including `emailVerified` |

Registration requires an 8–128 character password containing an uppercase letter, a lowercase letter, a digit, and a special character (punctuation or symbol; whitespace does not count). Access tokens expire after 15 minutes; refresh tokens expire after 30 days and rotate on use. The API stores only hashes of passwords and refresh tokens. Reusing a rotated refresh token revokes its entire session family. Refresh, reuse detection, and logout serialize through a PostgreSQL transaction advisory lock per family, so concurrent rotation cannot escape revocation. JWT authentication also requires a non-empty GUID `sub` claim. Logout revokes refresh sessions; issued access tokens remain valid until expiry. Use HTTPS outside local development and keep tokens out of logs.

## Workspace access control

Workspace routes under `/api/v1/workspaces` require a JWT. A new workspace's creator becomes Owner. Membership roles are loaded from PostgreSQL for every request, so role changes and removals take effect immediately. Non-members receive `404`; members without permission receive `403`.

| Role | Read workspace and members | Edit workspace or manage members | Manage projects | Write work items |
| --- | --- | --- | --- | --- |
| Owner | Yes | Yes | Yes | Yes |
| Admin | Yes | Yes, except Owner/Admin members | Yes | Yes |
| Manager | Yes | No | Yes, within assigned projects | Yes |
| Developer | Yes | No | No | Yes |
| Viewer | Yes | No | No | No |

Workspace routes support create/list/view/rename; member list/add/role change/removal; invitations; and ownership transfer. Direct member addition remains available to authorized Owners/Admins. Removing a member preserves their membership row, ends their project memberships, and clears project lead assignments. Rejoining the workspace does not restore old project access.

Owners cannot be removed or demoted through ordinary membership routes. `POST /api/v1/workspaces/{workspaceId}/ownership/transfer` accepts `{ "newOwnerUserId": "<uuid>" }`. Only the current Owner may call it; the target must be another active workspace member with a verified email. The old Owner becomes Admin. A workspace row lock serializes mutations; a deferred PostgreSQL constraint trigger enforces exactly one Owner at transaction commit. The new Owner may subsequently change the previous Owner's role.

## Password recovery by OTP

These endpoints are anonymous and share a limit of 10 requests per minute per IP:

1. `POST /api/v1/auth/forgot-password` with `{ "email": "you@example.com" }`. Returns `204` for existing, unknown, inactive, and cooldown-limited accounts. An active account receives a six-digit OTP through the existing Gmail SMTP configuration. Resends are limited to once per minute per account.
2. `POST /api/v1/auth/forgot-password/verify` with `{ "email": "you@example.com", "code": "012345" }`. A valid OTP returns `{ "resetToken": "...", "expiresAt": "..." }`. Codes expire after 10 minutes, allow five failed attempts, and can be confirmed only once.
3. `POST /api/v1/auth/reset-password` with `{ "resetToken": "...", "password": "NewPassword1!" }`. Requires the same password rules as registration. Returns `204`; the user signs in again. The reset token expires 10 minutes after OTP confirmation and is single use.

Requesting a new code invalidates previous recovery codes and reset grants. SMTP failures return `503` and preserve the previous challenge. OTPs and reset tokens are stored only as hashes in a separate recovery table; they cannot be used for email verification. Successful reset revokes all refresh sessions and preserves the account's email-verification state. Existing access JWTs retain their normal validity (up to 15 minutes), consistent with logout. Credential checks and reset serialize on the user row, and reset shares the existing refresh-family locks to prevent concurrent login/refresh from retaining an old session.

The sign-in help dialog implements email → OTP → new password → sign in, including resend cooldown and confirmation of the new password. Apply the `PasswordResetOtp` migration before using these endpoints. Registration, login response contracts, and email-verification requirements remain unchanged.

## Gmail SMTP email verification

Gmail SMTP connects directly to `smtp.gmail.com:587` with STARTTLS. No separate email service is needed. In `backend/.env`, fill in:

```dotenv
SMTP_USERNAME=
SMTP_APP_PASSWORD=
EMAIL_VERIFICATION_LIFETIME_MINUTES=10
INVITATION_LIFETIME_DAYS=7
```

The username is your full Gmail address. Enable Google 2-Step Verification and generate an App Password; use that password here, rather than your ordinary Gmail password. Both credential values are intentionally blank in the template. The local `.env` is ignored by Git. Compose maps these settings automatically. For `dotnet run`, set `Smtp__Username`, `Smtp__AppPassword`, `EmailVerification__LifetimeMinutes`, and `Invitations__LifetimeDays` as process environment variables; .NET does not load `.env` automatically.

If sending returns `503`, check the API's SMTP warning logs. A logged SMTP status `535` (`5.7.8`, BadCredentials) means Gmail rejected the configured username/App Password, even if TLS connects successfully. Create a new App Password for the exact `SMTP_USERNAME` account and update `backend/.env`; an ordinary Gmail password does not work. The sender removes formatting whitespace from grouped App Passwords. Never paste credentials or OTPs into logs or chat.

After changing `.env`, recreate the API container to load the new values (`docker compose up -d --build --no-deps --force-recreate api` from `backend/`); a container restart alone keeps its original environment. Logs report status/type without provider text, addresses, passwords, or email bodies. Network timeouts and missing SMTP settings are logged separately. A rejected send rolls back the new OTP and preserves the previous one.

Registration and sign-in remain available before email verification. After sign-in:

1. `POST /api/v1/auth/email-verification/request` with the Bearer token and no body sends a six-digit OTP code to the account's email.
2. Enter the six-digit code from that email and call `POST /api/v1/auth/email-verification/confirm` with `{ "code": "123456" }` and the same account's Bearer token.
3. `GET /api/v1/auth/me` reports `emailVerified: true`.

Email verification is required only once per account. The successful confirmation timestamp persists across sign-ins; requesting another code for an already verified account returns `204` without sending mail, even if SMTP is unavailable. Registration/sign-in do not automatically send codes.

Codes contain exactly six ASCII digits, including possible leading zeros, and expire after **10 minutes** by default (`EmailVerification:LifetimeMinutes`, configurable 1–30). Send the code as a string in the `code` field. Confirmations require the signed-in account and unchanged email snapshot. Codes are cryptographically generated and stored only as challenge/user-bound HMAC-SHA256 hashes using the JWT signing key with domain separation. Changing that key invalidates pending codes but does not reset verified accounts.

Each code allows **five failed attempts**; malformed values also count. Failed attempts and successful confirmation are serialized with a user row lock, so concurrent guesses cannot bypass the limit. A locked/expired code needs a resend. Resends have a **60-second cooldown**, including after lockout, and invalidate all previous codes. IP rate limiting also applies. SMTP failure rolls back the new code and restores the previous code's state. Missing configuration or delivery failure returns `503`. Codes are never returned through the API or written to application logs.

The `EmailVerificationOtp` migration adds the attempt counter and invalidates outstanding legacy verification tokens. Previously verified accounts stay verified; other accounts can request a new OTP. Gmail credentials are unchanged. This backend phase uses API confirmation; a browser form belongs to the future client.

## Workspace invitations

All routes require a Bearer token. New invitations queue automatic background email delivery when SMTP is configured; manual sharing remains available through the creation response.

| Method | Path | Request / behavior |
| --- | --- | --- |
| POST | `/api/v1/workspaces/{workspaceId}/invitations` | `email`, `role`; returns invitation metadata, raw `token`, and `invitationUrl` once |
| GET | `/api/v1/workspaces/{workspaceId}/invitations?page=1&pageSize=20` | Paginated metadata and status; never returns tokens or hashes |
| POST | `/api/v1/workspaces/{workspaceId}/invitations/{invitationId}/revoke` | Revoke a pending invitation |
| GET | `/api/v1/invitations/{invitationId}` | Invited account can inspect metadata; no token returned |
| POST | `/api/v1/invitations/accept` | `{ "token": "<invitation token>" }` |
| POST | `/api/v1/invitations/reject` | Same body; recipient rejection records `Revoked` |

Share the `invitationUrl` from the creation response privately. It points to the authenticated invitation metadata endpoint and carries the raw token in a URL fragment (`#token=...`), which is not sent to the server or included in HTTP request logs. Backend API users authenticate to inspect the invitation, then copy the fragment token into the accept/reject POST body. A future frontend can automate that flow; no browser UI is included. `PUBLIC_API_URL` controls the link base (default `http://localhost:8080`); host runs use `PublicApiUrl`. Use HTTPS outside local development. The URL and raw token are returned only in the creation response; the queued invitation email stores its body encrypted with ASP.NET Core Data Protection and removes that body after delivery or terminal failure. The recipient may register after the invitation was created, but must sign in and verify the exact invited email before accepting or rejecting. Tokens are cryptographically random and stored only as hashes. Expiry defaults to seven days (configurable 1–30 days). States are `Pending`, `Accepted`, `Expired`, and `Revoked`. Accepted and revoked outcomes remain terminal. Creating a replacement for an expired invitation preserves its displayed `Expired` history.

Only Owners/Admins may create/list invitations. Owners may invite Admins; Admins may invite Managers, Developers, and Viewers. Nobody may invite an Owner. Acceptance rechecks the inviter's current authority, email identity, expiry, and existing membership under a workspace lock, and commits invitation consumption and membership creation together. Duplicate pending invitations or active membership return `409`. Removed/demoted inviters cannot retain authority through outstanding invitations.

## Projects

Base path: `/api/v1/workspaces/{workspaceId}/projects`.

| Method | Path suffix | Behavior |
| --- | --- | --- |
| POST | `/` | Create; Owners/Admins only |
| GET | `/` | Paginated authorized project list |
| GET | `/{projectId}` | View project metadata |
| PUT | `/{projectId}` | Replace editable metadata with the current `version` |
| POST | `/{projectId}/archive` | Archive; idempotent |
| POST | `/{projectId}/restore` | Restore; idempotent |
| GET | `/{projectId}/members` | Paginated explicit member list |
| POST | `/{projectId}/members` | Add an active workspace member using `userId` |
| DELETE | `/{projectId}/members/{userId}` | End membership; retains history |

Owners/Admins see and manage every project. Managers must have explicit project membership before managing that project's metadata, archive state, and members. Developers and Viewers see only projects they belong to. Developers' resource-write permission is defined for Phase 3 work items; they cannot change project settings or membership. Viewers remain read-only. Owners/Admins have implicit access and need not appear in the explicit member list.

Create accepts `name`, `projectKey`, optional `description`, `leadUserId`, `startsOn`, and `targetOn`. Update accepts the same fields plus the last returned `version`; omitted optional fields are cleared. Names have 1–100 characters; descriptions at most 4000. Keys normalize to uppercase, contain 2–10 letters/digits starting with a letter, and remain unique within the workspace even when archived. Target dates cannot precede start dates. Leads must be active workspace members and automatically gain explicit project membership; changing a lead retains the former lead's membership. Clear/change the lead before removing them from the project.

List parameters: `search` (name/key), `archived` (omit for both states), `leadUserId`, `sort` (`name`, `key`, `createdAt`, `updatedAt`), `descending`, `page` (default 1), `pageSize` (default 20, maximum 100). The response contains `items`, `total`, `page`, and `pageSize`. Project-member and invitation lists use the same pagination bounds. Lists filter authorization before counting or returning data. Unavailable resources return `404`, insufficient privileges `403`, and conflicts `409`, using problem details.

Archived projects permit reads and restore but reject metadata/member mutations. Workspace removal may still revoke access and clear leads on archived projects. Progress and activity derive from persisted work items and activity records. Project keys cannot change after a work item has been created.

## Request validation

Request validators live under `FlowForge.Api/Validations`, grouped by Identity, Workspaces, Projects, WorkItems, Notifications, Realtime, and Workflows, with shared input and pagination rules. They return field-specific errors as `400 application/problem+json` using `ValidationProblem`. Endpoints check resource authorization before exposing validation errors for scoped resources. `ApiRules` handles identity, authorization, and business-state guards; database-dependent checks remain in the use-case flow. Configuration errors still return `503`, and state conflicts `409`.

Phase 4 notification queries use the shared `Validations/PaginationValidator.cs` directly: page 1–1,000,000, page size 1–100, with defaults of 1 and 20. ASP.NET Core rejects malformed integer/boolean query values before endpoint execution and validates GUID route/hub argument formats. `unreadOnly` is optional and defaults to no read-state filter. `Validations/Notifications/NotificationRequestValidator.cs` rejects an empty `notificationId` with a field-specific `400 ValidationProblem`; a non-empty ID that is absent or inaccessible still returns `404`. `Validations/Realtime/SubscriptionRequestValidator.cs` rejects empty `workspaceId`/`projectId` for all four join/leave methods with safe field-specific hub errors. Malformed GUIDs and missing hub arguments are rejected by SignalR binding. `read-all` has no input fields and does not need a separate validator. Hub subscription access and notification ownership remain authorization checks in their endpoint/service; they are not input validators. Background job option ranges are configuration validation in Infrastructure, enforced at startup by Data Annotations.

OTP format validation also uses a request validator, but malformed codes continue to consume an attempt inside the verification transaction. Logout remains idempotent for an empty refresh token. No database migration is needed for this validation refactor.

## Build and test
Executed checks and individual pass/fail outcomes are recorded in [the backend test results](TEST_RESULTS.md).

GitHub Actions runs these commands on pushes and pull requests with an isolated PostgreSQL service. Unit and smoke tests run without application secrets. PostgreSQL integration tests are skipped locally unless `FLOWFORGE_TEST_CONNECTION` is configured.

```sh
dotnet restore FlowForge.sln -m:1
dotnet build FlowForge.sln --no-restore -m:1 /p:UseSharedCompilation=false
dotnet test FlowForge.sln --no-restore -m:1 /p:UseSharedCompilation=false
```


To run all integration tests locally, point `FLOWFORGE_TEST_CONNECTION` at a **test PostgreSQL server** whose user can create databases:

```sh
export FLOWFORGE_TEST_CONNECTION='Host=localhost;Port=55432;Database=postgres;Username=postgres;Password=your-test-password'
dotnet test FlowForge.sln --no-restore -m:1 /p:UseSharedCompilation=false
```

The fixture creates a uniquely named database, applies real migrations, and drops only that database when finished. Tests cover email verification, invitation lifecycle and concurrency, ownership invariants and concurrent transfers, workspace/project isolation, project validation and pagination, archive behavior, stale updates, member removal, duplicate creation, and composite foreign keys. A test email sender captures OTP codes in memory; tests never send real email. Gmail delivery must be checked separately after you supply credentials.

## Phase 3: work items and collaboration

All Phase 3 routes require a Bearer token. A caller must have access to the project; Owners/Admins have implicit access, while Managers/Developers/Viewers need explicit project membership. Viewers can read but cannot change work resources. Archived projects and archived work items reject writes. Work-item changes and activity records commit in the same database transaction.

Base path: `/api/v1/workspaces/{workspaceId}/projects/{projectId}`.

| Resource | Routes |
| --- | --- |
| Statuses | `GET /statuses` returns the project's active statuses (`Todo`, `InProgress`, `InReview`, `Done`, `Blocked`). |
| Labels | `GET, POST /labels`; `PUT, DELETE /labels/{labelId}`. |
| Work items | `GET, POST /work-items`; `GET, PUT /work-items/{itemId}`; `POST /work-items/{itemId}/archive` and `/restore`. |
| Dependencies | `POST /work-items/{itemId}/dependencies` with `dependsOnItemId`; `DELETE /work-items/{itemId}/dependencies/{dependsOnId}`. |
| Comments | `GET, POST /work-items/{itemId}/comments`; `PUT, DELETE /work-items/{itemId}/comments/{commentId}`. |
| Time entries | `GET, POST /work-items/{itemId}/time-entries`; `PUT, DELETE /work-items/{itemId}/time-entries/{entryId}`. |
| Attachments | `GET, POST /work-items/{itemId}/attachments`; `GET, DELETE /work-items/{itemId}/attachments/{attachmentId}`. POST uses multipart form field `file`; GET by ID downloads the file. |
| Activity | `GET /api/v1/workspaces/{workspaceId}/activity`, optionally filtered by `projectId` and `workItemId` (requires `projectId`). |

Create and update work items with `title`, optional `description`, `type` (`Task` or `Issue`), `status`, `priority` (`Low`, `Medium`, `High`, `Critical`), optional `assigneeUserId`, `parentId`, `startDate`, `dueDate`, `estimateMinutes`, and `labelIds`. Updates also require the current `version`; stale versions return `409`. Label removal and dependency changes increment the affected work item's version. A project allocates item numbers atomically; its key cannot change once items exist. Assignees and mentions must have current project access. A returned item includes `isBlocked` if any dependency is unfinished and `needsReassignment` when its assignee loses access. Parent and dependency links stay within one project and cannot form cycles.

Work-item listing supports `search`, `status`, `priority`, `type`, `assigneeUserId`, `reporterUserId`, `labelId`, `dueBefore`, `dueAfter`, `archived`, `sort` (`createdAt`, `updatedAt`, `dueDate`, `priority`, `key`), `descending`, `page`, and `pageSize`. Paginated comments, time entries, attachments, and activity use `page` and `pageSize` (maximum 100). Activity is filtered to projects the caller can currently see. It returns safe before/after JSON and can be narrowed to one work item, including its comments, time entries, attachments, and dependency changes. Label changes on an item are retained in its audit record. Time-entry `startedAt` accepts ISO 8601 timestamps with offsets and normalizes them to UTC before persistence on create/update. Comments use `@{user-uuid}` syntax for server-validated mentions. Authors or project managers can edit/delete comments and time entries; edits/deletions retain activity history.

Attachments allow PNG, JPEG, PDF, and plain text up to 10 MiB. Metadata is in PostgreSQL, while bytes use a server-generated key in private storage. Configure `Attachments:StoragePath` outside the web root for host deployments. Docker Compose mounts `/app/private-attachments` in a persistent volume. Every download rechecks current project access. After a successful delete transaction, the private file is removed. If an upload transaction fails, its new file is removed; failed cleanup is logged without exposing the path to API clients.

Work-item creation, updates, status/assignment changes, archive/restore, and comment creation persist typed `domain_events` and corresponding pending `outbox_messages` in the same database transaction as the resource and activity record. They contain IDs and safe event metadata, not comment bodies or attachment content. Phase 4 dispatches these messages for notifications and real-time updates. Phase 5 independently schedules workflow execution from these durable events. Comment edits/deletions and new invitations also persist events in Phase 4.


## Phase 4: notifications and real-time updates

Notification APIs require a Bearer token. Queries always filter to the current recipient and their current workspace/project access before counting and paging. Pending invitation notifications are visible to the invited account even before workspace membership, provided the invitation and inviter remain valid. Revoked/expired/accepted invitations disappear from this list. Responses include resource IDs and an authorized API `resourceUrl`, without invitation tokens or comment bodies.

| Method | Path | Behavior |
| --- | --- | --- |
| GET | `/api/v1/notifications` | `page`, `pageSize` (maximum 100), optional `unreadOnly`; returns `items`, `total`, `page`, `pageSize`. |
| POST | `/api/v1/notifications/{notificationId}/read` | Marks the caller's visible notification as read; idempotent, otherwise `404`. |
| POST | `/api/v1/notifications/read-all` | Marks all currently visible notifications as read; idempotent. |

Notification rules:

- `Assigned`: notify the assignee when an item is created with an assignee or reassigned. Skip self-assignment and stale events whose assignee has already changed.
- `Mentioned`: notify users mentioned in a new comment, or newly added mentions on an edit. Repeated mentions and edits retaining a mention do not create duplicates. Skip self-mentions, deleted comments, removed mentions, and users without current project access.
- `Overdue`: notify the current assignee once per work item, due date, and recipient. An item becomes overdue after its due date ends in UTC. Completed work, archived projects/items, unassigned work, and inaccessible recipients are excluded. Changing the due date or assignee can produce a new reminder.
- `Invited`: notify an existing active account when a new invitation event is dispatched. Invitations sent before registration remain available through their emailed/manually shared link; there is no historical notification backfill on registration.

Notification emails are queued when SMTP is configured. A missing SMTP configuration does not prevent in-app notifications or invitation creation. OTP verification retains its existing immediate delivery behavior. Before every notification email send, the worker rechecks access, resource state, recipient identity, assignment/mention validity, and overdue state. Before invitation delivery, it rechecks expiry, revocation, acceptance, existing membership, and inviter authority. SMTP runs without holding a workspace lock, so slow delivery cannot block workspace writes. Notification emails contain only a generic sign-in prompt, with no resource IDs or private project details, including when processing bodies queued by an earlier version. Access changes during an in-flight send therefore cannot expose project data. Invitation links remain subject to the existing token, expiry, recipient, and inviter checks at acceptance.

Connect to SignalR at `/hubs/updates` with a JWT. Browser WebSocket/SSE clients may use `access_token` on this hub path; ordinary API routes do not accept query tokens. Connections close when the access token expires. Raw hosting request logs are replaced with built-in HTTP logging of method, path, status, and duration. Query strings, headers, and bodies are excluded, preserving request observability without logging access tokens.

| Hub method | Arguments | Behavior |
| --- | --- | --- |
| `JoinWorkspace` | `workspaceId` | Subscribe to updates for projects in this workspace that the user can currently access. |
| `LeaveWorkspace` | `workspaceId` | Leave that workspace subscription. |
| `JoinProject` | `workspaceId`, `projectId` | Subscribe after checking current project access. |
| `LeaveProject` | `projectId` | Leave that project subscription. |

The server sends `ProjectUpdated` with `eventId`, `type`, `workspaceId`, `projectId`, `entityId`, and `occurredAt`. Events cover existing work-item changes plus comment create/edit/delete and overdue events. Each authenticated active connection automatically receives its user's `NotificationReceived` event with only `{ id }`. Group names are chosen by the server. Delivery filters current authorized users, so previously joined groups cannot retain access after membership removal or role changes. The hub keeps no additional subscription registry. Overlapping workspace/project subscriptions can deliver the same event more than once; clients deduplicate using the stable `eventId`.

On reconnect, rejoin subscriptions and refetch work-item lists and `/api/v1/notifications`; there is no event replay endpoint. Subscribe before refetching, then deduplicate/invalidate using event IDs and refetch authoritative resource versions. Redis is not required for the current single-instance host; a distributed SignalR backplane is required before using multiple API instances for live delivery.

`NotificationWorker` owns overdue scanning, outbox dispatch, and email delivery through three independent loops with separate scopes, timeouts, intervals, and error handling. A failed overdue scan or slow SMTP delivery does not block the other jobs. PostgreSQL is the durable queue. Claims use row locks with `SKIP LOCKED`, claim IDs, and two-minute expirations. The worker commits notifications/email jobs before publishing SignalR updates, rechecks authorization under the existing workspace lock, and acknowledges only its own claim. Failed attempts back off (5, 10, 20, 40 seconds by default); permanent failures stop immediately and transient failures stop after five attempts. Failed rows retain safe errors and timestamps. Expired claims are recovered, and exhausted claims become terminal. Job errors are logged by ID/type without provider error messages or email bodies. SMTP sends retain their 20-second timeout; each scan, dispatch, or send attempt is bounded to 60 seconds.

Idempotency keys prevent duplicate persisted notifications and queued emails. SignalR is at least once during retries; clients should deduplicate `eventId`/notification `id`. SMTP cannot guarantee exactly one inbox delivery if a process stops after the provider accepted a message but before PostgreSQL records success. Successful, cancelled, and terminally failed email jobs discard their encrypted bodies. Earlier Phase 3 pending events are dispatched using current access/resource state. Existing invitations are not automatically emailed because their raw tokens were never retained.

| Host configuration | Default | Purpose |
| --- | --- | --- |
| `Notifications:Enabled` | `true` | Enable the background worker. Notification APIs/hub remain available when it is disabled. |
| `Notifications:PollSeconds` | `5` | Queue polling interval, 1–300 seconds. |
| `Notifications:OverdueIntervalMinutes` | `5` | Overdue scan interval, 1–1440 minutes. |
| `Notifications:BatchSize` | `50` | Maximum work per cycle/scan, 1–500. |
| `Notifications:MaxAttempts` | `5` | Attempt limit, 1–10. |
| `DataProtection:KeyPath` | ASP.NET Core default key storage | Persistent private directory for queued email encryption keys. |

Compose maps the first three settings through `NOTIFICATIONS_ENABLED`, `NOTIFICATIONS_POLL_SECONDS`, and `NOTIFICATIONS_OVERDUE_INTERVAL_MINUTES`, and persists Data Protection keys in the private `email_keys` volume. Keep this volume across container replacements. Host deployments should set `DataProtection__KeyPath` to a persistent private directory outside the web root; back up keys with the database so pending invitation emails remain decryptable after restore. SMTP credentials continue to use environment configuration.

The `PhaseFourNotifications` migration adds notifications and email delivery tables, outbox claim/failure fields, scope constraints/indexes, and optional project scope for workspace invitation events. Downgrade refuses to discard workspace-level event history that Phase 3 cannot represent; use a pre-upgrade backup if a full Phase 3 rollback is required.


## Phase 5: workflow engine

The backend implements scoped workflow management and validation, immutable versions, all seven triggers, typed ALL conditions, nine ordered action types, durable scheduling/execution, action-level idempotency, bounded retries, expiring claims, loop protection, activity history and authorized live updates. Owners/Admins manage workspace rules; Owners/Admins and assigned Managers manage project rules. Actions run under the creator's current project-write authority, rechecked before each action.

Use `/api/v1/workspaces/{workspaceId}/workflows` to list/create rules; `/{workflowId}` to view/update; `/{workflowId}/enable` or `/disable` to change state; and `/{workflowId}/executions` or `/executions/{executionId}` for history/details. New rules start disabled; edits require the current configuration `version`. Disabling cancels unfinished actions and preserves committed effects. Workflow APIs follow the existing authorization, pagination, ValidationProblem and optimistic-conflict conventions.

The `PhaseFiveWorkflows` migration preserves existing notification work and checkpoints historical events without replaying them into workflows. A separate scheduler checkpoint ensures notification dispatch cannot consume workflow events. Calendar triggers include unassigned items. Workflow execution, event scheduling, and calendar scanning have independent background loops; notification live delivery and workflow email use the existing notification worker.

See [workflow engine contracts and operations](../docs/workflow_engine.md) for the full JSON example, endpoint/condition/action contracts, versioning, permissions, date semantics, disable/recovery behavior, email delivery diagnostics, configuration, and migration rollback limits. The frontend now includes the structured workflow builder and execution views; see [Phase 5 frontend](../docs/phase5_frontend.md). The read-only `/api/v1/workspaces/{workspaceId}/workflows/executions/{executionId}` lookup supports Activity links and enforces the same workflow-management permission as nested execution details.


### Live workflow HTTP checks

`tests/FlowForge.Api.SmokeTests` is a .NET console runner built with the solution. Start a disposable Release API/database with workflow and notification workers enabled, then run from the repository root:

```sh
dotnet run --project backend/tests/FlowForge.Api.SmokeTests -- \
  --base-url http://localhost:8080 \
  --output backend/test-results/phase5/live-http.json
```

It creates disposable accounts/workspace/project/workflows, checks all nine workflow operations through real HTTP, verifies 47 HTTP responses plus 19 state assertions, and archives its project. It writes a sanitized JSON ledger even on an unexpected response and exits nonzero on failure. Polling requests are recorded separately. This runner uses only the .NET SDK and standard libraries; it runs explicitly after starting the API. See [test results](TEST_RESULTS.md) for the latest run and parity evidence.

## Phase 6: project dashboard (backend only)

All dashboard routes require existing project read access, including for archived projects:

- `GET /api/v1/workspaces/{workspaceId}/projects/{projectId}/dashboard`: work-item totals, completion, counts by status/priority, overdue count and ten recent activity records.
- `GET /api/v1/workspaces/{workspaceId}/projects/{projectId}/dashboard/workload`: paginated unfinished item counts and estimate-minute totals per assignee, including Unassigned where applicable.
- `GET /api/v1/workspaces/{workspaceId}/projects/{projectId}/dashboard/overdue`: paginated unfinished items due before UTC today, including unassigned work.

Only non-archived work items contribute to metrics; tasks, issues and subtasks each count once. Completion uses status category `Done`; empty projects return 0%. Each response reads a consistent database snapshot. Metrics are computed from existing data with no new migration or worker. Pagination defaults to page 1/size 20, with maximum size 100. See [dashboard definitions and contracts](../docs/dashboard.md) and [verification results](TEST_RESULTS.md).

This slice excludes AI, milestones/documents, advanced reporting metrics and dashboard frontend work.

## Phase 6: AI extension points (no model configured)

The subsequent backend slice adds provider-independent AI APIs under `/api/v1/workspaces/{workspaceId}/projects/{projectId}/ai`:

- `POST /tasks/propose`: validate a feature description, return editable proposals and an expiring preview token; create no tasks.
- `POST /tasks/accept`: explicitly accept edited/selected proposals; atomically create normal tasks/dependencies, audit and outbox with idempotent confirmation.
- `POST /summary`: return authoritative project metrics separately from a validated AI narrative; empty projects return `InsufficientData` without a provider call.

Ollama Cloud is supported by the built-in HTTP adapter; no model or API key is selected/supplied. Set `OLLAMA_API_KEY` and `OLLAMA_MODEL` yourself in `backend/.env` for Compose, then run `docker compose up --build -d api`. For a host .NET process use `Ollama__ApiKey` and `Ollama__Model` instead; `.env` is not automatically loaded. Both values default to empty, and generation remains unavailable (`503`) until both are configured. No local Ollama install, provider SDK, migration or extra dependency is required. The interface remains replaceable. See [configuration, adapter behavior and JSON contracts](../docs/ai_backend.md). Milestones/documents, advanced metrics and AI frontend screens remain deferred.
