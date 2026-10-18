# FlowForge --- Feature Specification

**Purpose:** Functional baseline for coding agents implementing
FlowForge, a project-management platform with configurable workflow
automation.

## 1. Product Scope

FlowForge lets teams organize work into workspaces, projects, tasks, and
issues. It supports collaboration, role-based access, real-time updates,
notifications, reporting, and optional AI assistance. Its main
differentiator is a workflow engine that executes user-defined triggers,
conditions, and actions.

**Architecture:** Modular monolith using Clean Architecture principles.
Do not introduce microservices for the initial release.

**Out of scope initially:** Source-control hosting, automatic
pull-request creation/merging, arbitrary user-written workflow code,
billing, and enterprise SSO.

## 2. Users and Permissions

Default workspace roles: - **Owner:** Full workspace control, including
ownership transfer. - **Admin:** Manages members, settings, and
projects, subject to Owner-only restrictions. - **Manager:** Manages
assigned projects and their work. - **Developer:** Creates and updates
work items and collaborates. - **Viewer:** Read-only access.

Requirements: - Enforce permissions on the server for APIs, files,
SignalR subscriptions, workflows, and AI context. - Users may only
access workspaces and projects they are authorized to access. - Never
rely on frontend visibility as an authorization control. - Preserve
historical records when a member is removed. - Use consistent `401`,
`403`, `404`, and validation-error responses without leaking restricted
resource details.

## 3. Authentication

Phase 2 adds Gmail SMTP email verification using a six-digit OTP code, expiring after 10 minutes by default, with a maximum of five failed attempts. Codes are stored only as keyed hashes, and resends invalidate previous codes with a 60-second cooldown. Users verify once; their verified state persists across sign-ins. Existing accounts remain unverified until confirmation. Verification is required before accepting invitations or receiving workspace ownership. SMTP credentials are supplied through environment configuration, with empty defaults.

Implement: - Email/password registration and sign-in. - Secure password
hashing; never store plaintext passwords. - Short-lived JWT access
tokens and rotating refresh tokens. - Logout and refresh-token/session
revocation. - Token validation, sensitive-endpoint rate limiting, and
generic invalid-credential errors. - Password reset with expiring,
single-use tokens if email delivery is configured.

OAuth is a later enhancement, not required for the first release.

**Acceptance criteria:** Expired or revoked refresh tokens cannot obtain
new access tokens, and authentication never bypasses resource
authorization.

## 4. Workspace Management

Features: - Create, list, view, and update workspaces. - Invite users by
email; accept or reject invitations. - Assign or change workspace roles
and remove members. - View members and invitation status.

Invitations use manually shared links in Phase 2, with tokens in URL fragments; the backend exposes authenticated metadata and POST acceptance, and a future client can automate the browser flow. Phase 4 also queues automatic email delivery for newly created invitations when SMTP is configured. Default expiry is configurable, initially seven days. Acceptance requires a signed-in account with the invited email verified, rechecks the inviter's authority, and atomically creates membership and consumes the invitation. Lifecycle: Pending, Accepted, Expired, Revoked; recipient rejection records Revoked.

Ownership transfer is restricted to the current Owner. The target must be an active same-workspace member with a verified email. The previous Owner becomes Admin. Serialize concurrent transfers and enforce exactly one Owner at commit.

Rules: - The creator becomes Owner. - A workspace must have exactly one
active Owner. - Prevent duplicate active membership. - Invitations
expire and can be used only once. - Removing a member must not delete
their historical comments or activity. Flag their assigned work for
reassignment where applicable.

## 5. Project Management

Features: - Create, view, update, archive, restore, and list projects. -
Configure name, description, unique project key, dates, and project
lead. - View project members, overview, progress, recent activity, and
overdue work. - Search, filter, and sort projects.

Access: Owners/Admins access all projects and exclusively create projects. Managers manage only explicitly assigned projects. Developers/Viewers require explicit project membership; Developers can modify project work resources, while Viewers are read-only. Project settings and membership management are limited to Owners/Admins and assigned Managers.

Rules: - Each project belongs to one workspace. - Project keys are
unique within a workspace. - Project leads must be authorized workspace
members. - Archived projects are read-only by default. - Calculate
progress from work-item states rather than storing an independently
editable percentage.

## 6. Tasks and Issues

Support task/issue types and these fields: - Unique ID and readable key
(for example, `FLOW-123`). - Title, description, type, status, priority,
reporter, optional assignee. - Labels, due date, optional start date and
estimate. - Parent/subtask relationship and dependencies. - Comments,
attachments, activity history, and time entries. - Created/updated
timestamps.

Core operations: - Create, view, update, archive, assign, and search
work items. - Change status, priority, due date, and labels. -
Filter/sort by status, priority, assignee, reporter, label, and due
date. - Create subtasks and dependencies; identify blocked work. - Add
comments, mention members, upload/download attachments, and record time.

Default statuses: `Todo`, `InProgress`, `InReview`, `Done`, `Blocked`.
Keep status handling extensible for future workflow configuration.

Rules: - Validate all referenced users and resources against the item's
workspace/project. - Reject self-dependencies, dependency cycles, and
parent-child cycles. - Status changes must pass through business logic
and respect applicable workflow rules. - Use optimistic concurrency
where practical; conflicting updates must not silently overwrite newer
data. - Persist important work-item changes and activity records
consistently.

## 7. Comments and Attachments

Comments: - Add comments to work items. - Allow editing/deletion by the
author and authorized moderators according to policy. - Record author
and timestamps; preserve an audit trail for edits/deletions. - Resolve
`@mentions` to real users on the server and notify only authorized
recipients.

Attachments: - Store file metadata and a storage key, not a
client-controlled path. - Enforce file-size and content-type limits. -
Authorize every download against the parent work item. - Do not expose
private storage locations publicly.

## 8. Workflow Automation Engine

The workflow engine is the primary product differentiator. A workflow
has a name, scope (workspace and optional project), enabled state,
trigger, conditions, ordered actions, creator, timestamps, configuration
version, and execution history.

### Triggers

Implement: - Work item created or updated. - Work-item status changed. -
Work item assigned/unassigned. - Due date reached/overdue. - Comment
added.

### Conditions

Support a typed, validated condition model: - Field equals/does not
equal a value. - Field is in a list. - Field is empty/not empty. -
Priority comparison. - Due-date comparison. - Actor, assignee, label,
status, or item type matches.

Support `ALL` (AND); `ANY` (OR) is optional. Reject invalid
field/operator/value combinations. Never evaluate arbitrary
user-supplied code.

### Actions

Implement: - Assign/unassign to a valid member. - Change status or
priority. - Add/remove label. - Add a comment/system note. - Send in-app
notification. - Send email if configured.

Release-note creation can be added later.

### Execution and reliability

Each execution records workflow/version, triggering event, related
resource IDs, status, timestamps, attempts, safe error details, and
action outcomes where practical. States: `Pending`, `Running`,
`Succeeded`, `Failed`, `Cancelled`.

Requirements: - Execute asynchronously through a background job
mechanism. - Use a reliable event-to-job handoff, such as an outbox,
where needed to prevent lost events. - Implement idempotency, bounded
retries with backoff, timeouts, and maximum attempts. - Do not endlessly
retry validation or permission failures. - Prevent recursive workflow
loops using correlation/origin metadata or execution-depth limits. -
Validate configuration before enabling a workflow. - Enforce workflow
scope and permissions for every action. - Define what happens to running
executions when a workflow is disabled.

### Phase 5 agreed backend policy

Phase 5 delivers backend/API support; the workflow builder UI is deferred with the frontend. Owners/Admins manage workspace rules. Owners/Admins and explicitly assigned Managers manage project rules. Execution uses the creator's current project resource-write authority, rechecked before every action; creator and scope are immutable.

Pin the configuration version at scheduling. Evaluate ALL conditions against current item state once when execution starts; the actor comes from the triggering event. Disabling prevents new scheduling and cancels unfinished actions while preserving committed actions. Enablement does not replay earlier events. Retries preserve completed actions; action effects, audit and completion records commit atomically. Generated events carry server-owned execution ancestry and bounded depth to stop recursive rules.

The implemented API, exact trigger/condition/action vocabulary, UTC calendar semantics, email delivery diagnostics, configuration and migration behavior are specified in [workflow_engine.md](workflow_engine.md).

### Workflow UI/API

Authorized users can create/edit workflows, select triggers, add/reorder
conditions and actions, validate, enable/disable, and inspect execution
history. A structured form-based builder is sufficient; a drag-and-drop
canvas is optional.

**Acceptance criteria:** Matching events schedule execution; conditions
determine whether actions run; failures are diagnosable; retries do not
duplicate idempotent actions; unauthorized users cannot manage or
inspect workflows outside their scope.

## 9. Real-Time Updates and Notifications

Use SignalR for authorized real-time events, including work-item
changes, comments, notifications, and workflow execution updates.

Requirements: - Validate access before joining workspace/project groups;
group names alone are not authorization. - Send only necessary event
data; clients can fetch full resources through the API. - Support
reconnect by refetching current state. - Use a Redis backplane only if
multi-instance deployment requires it.

Notifications: - In-app notifications, real-time delivery, and optional
email. - Events include assignment, mentions, overdue work, invitations,
and relevant workflow outcomes. - Users can list notifications, mark
one/all as read, and navigate to related resources. - Send email in the
background, retry transient failures, and deduplicate where
appropriate. - Do not send private project details to users who no
longer have access.

Phase 4 delivery rules: assignment notifications target the current assignee, mention notifications target newly mentioned authorized users, and both skip self-notifications. Overdue notifications target the current assignee once per work item/due date/recipient after the due date ends in UTC; exclude completed work and archived items/projects. Notification emails follow these rules whenever SMTP is configured. New invitations also queue email; existing active accounts receive in-app invitation notifications. Recheck current permissions and relevant resource/invitation state before delivery. Notification reads use current resource access. Client recovery uses subscription rejoin and REST refetch, with event/notification ID deduplication. See the backend README for endpoint and hub contracts.

## 10. Activity and Audit Log

Record important changes, including workspace membership, project
changes, work-item changes, comment edits/deletions, workflow
configuration, and workflow outcomes.

Each record should include actor (user or system), action, entity
type/ID, UTC timestamp, workspace/project context, safe before/after
values where applicable, and correlation/workflow execution ID.

Audit records are append-only through normal application operations.
Never log passwords, tokens, secrets, or sensitive attachment content.
Provide an authorized, paginated activity feed.

## 11. Dashboard and Reporting

Project dashboard: - Work-item counts by status and priority. -
Completion percentage. - Overdue work. - Workload by assignee. - Recent
updates/activity. - Milestone progress.

Advanced metrics may include velocity, burndown, cycle time, and lead
time. Define each metric before displaying it and collect the required
historical data. Calculate numeric metrics in application/database
logic, not through AI.

## 12. Milestones and Documents

Deliver after core work-item management if scope requires: -
Create/update/archive project milestones with target dates and
associated work items. - Calculate milestone completion from associated
work. - Create/edit project documents with access control and
timestamps. - Record important changes in activity history.

Rich-text editing and document version history are optional
enhancements.

## 13. AI Features

AI is optional; the platform must work without an AI provider.

### AI Task Assistant

-   Accept a feature description and return structured task proposals
    with titles, descriptions, suggested priorities, and possible
    dependencies.
-   Validate the output on the server.
-   Show a preview and allow edits.
-   Persist tasks only after explicit user acceptance.
-   Handle timeouts, rate limits, malformed output, and missing
    credentials.

### AI Project Summary

Use only context the user is authorized to access. Summarize progress,
completed work, blockers, overdue work, and risks. Distinguish facts
from AI-generated interpretations; calculate metrics in application
code. Handle insufficient data gracefully.

Never expose cross-workspace information through AI prompts, caches, or
retrieval indexes.

## 14. Background Jobs and Search

Background jobs may handle workflow execution, email delivery, overdue
checks, and cleanup of expired invitations/tokens.

-   Make jobs observable and idempotent.
-   Retry only transient failures with bounded retries.
-   Use UTC internally and prevent overlapping scheduled jobs from
    creating duplicates.
-   Keep secrets out of logs.

For list APIs: - Use server-side pagination, filtering, and sorting. -
Allowlist sort fields. - Enforce authorization before returning
results. - Add indexes based on actual query patterns.

## 15. API, Data, and Security Standards

-   Use versionable REST endpoints, DTOs, input validation, and OpenAPI
    documentation.
-   Do not expose persistence entities directly.
-   Use asynchronous I/O and cancellation tokens for request-driven
    operations.
-   Return consistent problem-details errors; never expose production
    stack traces.
-   Use EF Core migrations, foreign keys, unique constraints, explicit
    delete behavior, and UTC timestamps.
-   Use transactions for related changes and keep activity records
    consistent with business operations.
-   Store secrets outside source control, use HTTPS in deployment,
    configure CORS narrowly, and redact sensitive logs.
-   Validate uploads and rate-limit sensitive endpoints.
-   Add health checks and structured logs with correlation IDs.

## 16. Architecture and Technology Stack

-   **Backend:** ASP.NET Core 10 Web API, Entity Framework Core.
-   **Database:** PostgreSQL.
-   **Cache/coordination:** Redis where justified.
-   **Real-time:** SignalR.
-   **Background processing:** Hangfire and/or `BackgroundService`;
    assign clear ownership for each job type.
-   **Authentication:** JWT access tokens and refresh tokens.
-   **Frontend:** React, TypeScript, TanStack Query, Zustand, shadcn/ui.
-   **Infrastructure:** Docker, Docker Compose, Nginx, GitHub Actions.
-   **Tests:** Unit, integration, API, and selected end-to-end tests.

Suggested modules: Identity, Workspace, Project, WorkItems, Comments,
Attachments, Workflow, Notifications, Activity, Reporting, AI. Keep
module boundaries explicit and avoid circular dependencies.

## 17. Testing

Unit tests: - Permissions and workspace isolation. - Work-item state
transitions and dependency-cycle validation. - Workflow condition/action
evaluation, retries, and idempotency. - Progress/reporting calculations.

Integration tests: - Authentication and authorization boundaries. -
Database constraints and transaction consistency. - Work-item changes
and activity history. - Workflow persistence/execution. - Notification
persistence and file-access authorization.

End-to-end critical journeys: 1. Sign in and create a workspace. 2.
Invite and accept a member. 3. Create a project and work item. 4. Assign
work and add a comment. 5. Configure and execute a workflow. 6.
Receive/read a notification. 7. Verify cross-workspace access is denied.

Tests must run in CI without production credentials or unavailable
external services.

## 18. Delivery Plan

### Phase 1 --- Foundation

Repository structure, API/client, PostgreSQL, migrations, Docker
Compose, authentication, baseline tests, and CI.

**Done when:** The app starts locally, users can sign in, and baseline
tests pass.

### Phase 2 --- Workspace and Project

Membership, invitations, roles, authorization, projects, project keys,
and archive behavior.

**Done when:** Workspace isolation and permission tests pass.

### Phase 3 --- Work Items and Collaboration

Tasks/issues, assignments, labels, dependencies, subtasks, comments,
attachments, activity, and list filtering.

**Done when:** Core project-management journeys work end to end.

### Phase 4 --- Notifications and Real-Time

In-app/email notifications, SignalR, overdue checks, and job failure
handling.

**Done when:** Authorized users receive relevant updates and clients can
recover after reconnecting.

### Phase 5 --- Workflow Engine

Triggers, conditions, actions, async execution, execution history,
retries, idempotency, and loop protection.

**Done when:** A configured rule reliably reacts to events and exposes
execution outcomes.

### Phase 6 --- Dashboard and AI

Project metrics, optional milestones/documents, AI task breakdown, and
AI project summary.

**Done when:** Metrics follow documented definitions and AI-generated
tasks require user acceptance.

### Phase 7 --- Production Readiness

Security review, critical tests, logs, health checks, CI/CD, deployment,
and documentation.

**Done when:** A new developer can run the app from the README and CI
passes.

## 19. Coding Agent Instructions

Implement incrementally as vertical slices; do not generate the whole
system in one change.

For each slice: 1. Inspect the repository and follow existing
conventions. 2. Identify module, business rules, API contracts,
persistence changes, and tests. 3. Check authorization and
workspace/project isolation. 4. Implement validation and consistent
error handling. 5. Add tests for success, validation failure,
authorization failure, and edge cases. 6. Update migrations and
documentation. 7. Run relevant tests and report actual results.

Do not: - Invent features or silently change requirements. - Use
placeholders for security, authorization, workflow execution, or
persistence. - Trust client-side permissions. - Execute arbitrary code
from workflow definitions or AI output. - Introduce microservices
without an explicit requirement. - Commit secrets or log sensitive data.

When a requirement is ambiguous, state the assumption and choose the
simplest safe behavior. For each completed slice, report changed files,
migrations, tests run, and known limitations.
