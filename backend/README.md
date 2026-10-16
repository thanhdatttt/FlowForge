# FlowForge backend

This is the .NET 10 modular-monolith backend. It includes authentication, Gmail SMTP email verification, workspaces, invitations, ownership transfer, projects, OpenAPI, and health checks.

## Projects

- `src/FlowForge.Api`: HTTP host, module endpoints, and client-facing request/response DTOs in `Contracts/`.
- `src/FlowForge.Application`: use cases and module contracts.
- `src/FlowForge.Domain`: business rules and domain models.
- `src/FlowForge.Infrastructure`: EF Core context and PostgreSQL provider; future persistence and external integrations.
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

Registration requires an 8–128 character password containing an uppercase letter, a lowercase letter, a digit, and a special character (punctuation or symbol; whitespace does not count). Access tokens expire after 15 minutes; refresh tokens expire after 30 days and rotate on use. The API stores only hashes of passwords and refresh tokens. Reusing a rotated refresh token revokes its entire session family. Logout revokes refresh sessions; issued access tokens remain valid until expiry. Use HTTPS outside local development and keep tokens out of logs.

## Workspace access control

Workspace routes under `/api/v1/workspaces` require a JWT. A new workspace's creator becomes Owner. Membership roles are loaded from PostgreSQL for every request, so role changes and removals take effect immediately. Non-members receive `404`; members without permission receive `403`.

| Role | Read workspace and members | Edit workspace or manage members | Manage projects | Write work items (future API) |
| --- | --- | --- | --- | --- |
| Owner | Yes | Yes | Yes | Yes |
| Admin | Yes | Yes, except Owner/Admin members | Yes | Yes |
| Manager | Yes | No | Yes, within assigned projects | Yes |
| Developer | Yes | No | No | Yes |
| Viewer | Yes | No | No | No |

Workspace routes support create/list/view/rename; member list/add/role change/removal; invitations; and ownership transfer. Direct member addition remains available to authorized Owners/Admins. Removing a member preserves their membership row, ends their project memberships, and clears project lead assignments. Rejoining the workspace does not restore old project access.

Owners cannot be removed or demoted through ordinary membership routes. `POST /api/v1/workspaces/{workspaceId}/ownership/transfer` accepts `{ "newOwnerUserId": "<uuid>" }`. Only the current Owner may call it; the target must be another active workspace member with a verified email. The old Owner becomes Admin. A workspace row lock serializes mutations; a deferred PostgreSQL constraint trigger enforces exactly one Owner at transaction commit. The new Owner may subsequently change the previous Owner's role.

## Gmail SMTP email verification

Gmail SMTP connects directly to `smtp.gmail.com:587` with STARTTLS. No separate email service is needed. In `backend/.env`, fill in:

```dotenv
SMTP_USERNAME=
SMTP_APP_PASSWORD=
EMAIL_VERIFICATION_LIFETIME_MINUTES=10
INVITATION_LIFETIME_DAYS=7
```

The username is your full Gmail address. Enable Google 2-Step Verification and generate an App Password; use that password here, rather than your ordinary Gmail password. Both credential values are intentionally blank in the template. The local `.env` is ignored by Git. Compose maps these settings automatically. For `dotnet run`, set `Smtp__Username`, `Smtp__AppPassword`, `EmailVerification__LifetimeMinutes`, and `Invitations__LifetimeDays` as process environment variables; .NET does not load `.env` automatically.

Registration and sign-in remain available before email verification. After sign-in:

1. `POST /api/v1/auth/email-verification/request` with the Bearer token and no body sends a six-digit OTP code to the account's email.
2. Enter the six-digit code from that email and call `POST /api/v1/auth/email-verification/confirm` with `{ "code": "123456" }` and the same account's Bearer token.
3. `GET /api/v1/auth/me` reports `emailVerified: true`.

Email verification is required only once per account. The successful confirmation timestamp persists across sign-ins; requesting another code for an already verified account returns `204` without sending mail, even if SMTP is unavailable. Registration/sign-in do not automatically send codes.

Codes contain exactly six ASCII digits, including possible leading zeros, and expire after **10 minutes** by default (`EmailVerification:LifetimeMinutes`, configurable 1–30). Send the code as a string in the `code` field. Confirmations require the signed-in account and unchanged email snapshot. Codes are cryptographically generated and stored only as challenge/user-bound HMAC-SHA256 hashes using the JWT signing key with domain separation. Changing that key invalidates pending codes but does not reset verified accounts.

Each code allows **five failed attempts**; malformed values also count. Failed attempts and successful confirmation are serialized with a user row lock, so concurrent guesses cannot bypass the limit. A locked/expired code needs a resend. Resends have a **60-second cooldown**, including after lockout, and invalidate all previous codes. IP rate limiting also applies. SMTP failure rolls back the new code and restores the previous code's state. Missing configuration or delivery failure returns `503`. Codes are never returned through the API or written to application logs.

The `EmailVerificationOtp` migration adds the attempt counter and invalidates outstanding legacy verification tokens. Previously verified accounts stay verified; other accounts can request a new OTP. Gmail credentials are unchanged. This backend phase uses API confirmation; a browser form belongs to the future client.

## Workspace invitations

Phase 2 uses manual sharing; automatic invitation email delivery remains in Phase 4. All routes require a Bearer token.

| Method | Path | Request / behavior |
| --- | --- | --- |
| POST | `/api/v1/workspaces/{workspaceId}/invitations` | `email`, `role`; returns invitation metadata, raw `token`, and `invitationUrl` once |
| GET | `/api/v1/workspaces/{workspaceId}/invitations?page=1&pageSize=20` | Paginated metadata and status; never returns tokens or hashes |
| POST | `/api/v1/workspaces/{workspaceId}/invitations/{invitationId}/revoke` | Revoke a pending invitation |
| GET | `/api/v1/invitations/{invitationId}` | Invited account can inspect metadata; no token returned |
| POST | `/api/v1/invitations/accept` | `{ "token": "<invitation token>" }` |
| POST | `/api/v1/invitations/reject` | Same body; recipient rejection records `Revoked` |

Share the `invitationUrl` from the creation response privately. It points to the authenticated invitation metadata endpoint and carries the raw token in a URL fragment (`#token=...`), which is not sent to the server or included in HTTP request logs. Backend API users authenticate to inspect the invitation, then copy the fragment token into the accept/reject POST body. A future frontend can automate that flow; no browser UI is included. `PUBLIC_API_URL` controls the link base (default `http://localhost:8080`); host runs use `PublicApiUrl`. Use HTTPS outside local development. The URL and raw token are returned only in the creation response. The recipient may register after the invitation was created, but must sign in and verify the exact invited email before accepting or rejecting. Tokens are cryptographically random and stored only as hashes. Expiry defaults to seven days (configurable 1–30 days). States are `Pending`, `Accepted`, `Expired`, and `Revoked`. Accepted and revoked outcomes remain terminal. Creating a replacement for an expired invitation preserves its displayed `Expired` history.

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

Archived projects permit reads and restore but reject metadata/member mutations. Workspace removal may still revoke access and clear leads on archived projects. Progress, work-item resources, overdue work, and activity feeds depend on Phase 3; no fabricated progress is returned. Project keys may currently be edited; Phase 3 must prevent key changes after work items exist unless aliases are implemented.

## Request validation

Request validators live under `FlowForge.Api/Validation`, grouped by Identity, Workspaces, and Projects, with shared input and pagination rules. They return field-specific errors as `400 application/problem+json` using `ValidationProblem`. Endpoints check resource authorization before exposing validation errors for scoped resources. `ApiRules` handles identity, authorization, and business-state guards; database-dependent checks remain in the use-case flow. Configuration errors still return `503`, and state conflicts `409`.

OTP format validation also uses a request validator, but malformed codes continue to consume an attempt inside the verification transaction. Logout remains idempotent for an empty refresh token. No database migration is needed for this validation refactor.

## Build and test

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
