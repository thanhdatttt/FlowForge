# Phase 5: workflow engine backend

Phase 5 implements the backend/API. The React workflow builder is deferred with the rest of the frontend. Definitions use validated JSON; arbitrary expressions, scripts, OR conditions, drag-and-drop configuration, and release-note actions are outside this release.

## Permissions and lifecycle

Owners/Admins manage workspace-wide rules and every project rule. Managers manage rules only in projects with explicit membership. Developers/Viewers cannot manage or inspect workflow definitions or execution history. Inaccessible workflow IDs return 404. Lists filter before counting/paging.

The creator remains the execution principal even if another authorized manager edits the rule. Before every action, the worker rechecks the creator's current project resource-write permission and the current item/project archive state, under the existing workspace lock. Removed/inactive creators and read-only creators cannot retain privileges through automation. Losing workflow-management authority alone does not revoke an independently retained project resource-write permission. Targets must have current project access.

New workflows start disabled. Project/workspace scope and creator cannot change. PUT requires the current `version`, creates an immutable configuration version, and preserves the enabled state. Existing scheduled executions retain their version. Updates with stale versions return 409. Enable/disable require the current configuration version and are idempotent. Enable revalidates current action targets; project rules cannot be created, edited, or enabled on archived projects. Disabling remains available.

Enablement starts a new event eligibility window. Events from before the most recent enablement are not replayed, including unprocessed events from a disabled interval. Disabling serializes with actions: it cancels unfinished executions/actions, preserves committed actions, and does not undo changes or revoke already committed email queue jobs. An action in progress commits before disable obtains the workspace lock, or sees disable before doing work. A normal archived item/project cancels its unfinished execution.

## API

Base: `/api/v1/workspaces/{workspaceId}/workflows`; all operations require Bearer authentication.

| Method | Suffix | Contract |
| --- | --- | --- |
| GET | `/` | List manageable rules; optional `projectId`, `page`, `pageSize`. |
| POST | `/` | Create a validated, disabled workflow. |
| POST | `/validate` | Validate the same write body without persisting it. |
| GET | `/{workflowId}` | Current metadata and definition. |
| PUT | `/{workflowId}` | Replace name/definition; unchanged `projectId` and current `version` required. |
| POST | `/{workflowId}/enable` | Body `{ "version": 1 }`. |
| POST | `/{workflowId}/disable` | Body `{ "version": 1 }`; cancel unfinished actions. |
| GET | `/{workflowId}/executions` | Paginated history. |
| GET | `/{workflowId}/executions/{executionId}` | Pinned version ID/number and full definition, triggering event/actor, correlation/origin/depth, item/project, state, condition result, attempts, timestamps, ordered action results, safe errors, and queued workflow email delivery states. |

Pagination follows existing defaults (page 1, page size 20, maximum size 100). Validation returns field-specific `400 application/problem+json`. A successful validation response is `{ "valid": true }`.

Example write body (substitute actual project/user IDs and unique action UUIDs):

```json
{
  "name": "Assign critical work",
  "projectId": "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
  "definition": {
    "trigger": "WorkItemCreated",
    "conditions": [
      { "field": "Priority", "operator": "Equals", "values": ["Critical"] }
    ],
    "actions": [
      { "id": "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb", "type": "Assign", "userId": "cccccccc-cccc-cccc-cccc-cccccccccccc" },
      { "id": "dddddddd-dddd-dddd-dddd-dddddddddddd", "type": "AddComment", "value": "Critical work was assigned automatically." }
    ]
  }
}
```

Omit `projectId` or use null for a workspace rule. On PUT, include `version` alongside `name`, `projectId`, and `definition`. Read responses expose the configuration as `definition` and its version as `version`. Execution responses expose the pinned definition/version, so edits cannot hide the configuration that actually ran.

## Triggers and conditions

Triggers: `WorkItemCreated`, `WorkItemUpdated`, `WorkItemStatusChanged`, `WorkItemAssignmentChanged` (assignment and unassignment), `CommentAdded`, `WorkItemDueDateReached`, `WorkItemDueDateOverdue`. Work-item PUTs may emit an update plus specific status/assignment events; different rules may intentionally react to each. No-op automation actions emit no item-update event.

Conditions use ALL/AND, with an empty array matching every eligible event. Evaluate once against current item state when the execution starts, after permission checks; persist the result across retries. `Actor` comes from the triggering event. The full condition model supports at most 20 conditions:

| Field | Values |
| --- | --- |
| `Priority` | `Low`, `Medium`, `High`, `Critical`, in that order. |
| `Status`, `Label` | Exact project status/label names; label equality means membership. |
| `Type` | `Task` or `Issue`. |
| `Assignee`, `Actor` | Non-empty user UUIDs; absent actor/assignee counts as empty. |
| `DueDate` | Exact Gregorian `yyyy-MM-dd` calendar dates, independent of host culture. |
| `Title`, `Description` | Exact text; values are limited to 200 characters. |

Operators: `Equals`, `NotEquals`, `In`, `IsEmpty`, `IsNotEmpty`. Only `Priority`/`DueDate` additionally allow `GreaterThan`, `LessThan`, `GreaterThanOrEqual`, `LessThanOrEqual`. Empty operators require `values: []`; `In` accepts 1–50 values; other operators accept exactly one. An empty field satisfies `NotEquals`, but does not satisfy an ordered comparison.

Date triggers scan active, unfinished items whether assigned or unassigned. Reached means `dueDate == UTC today`; overdue means `dueDate < UTC today`. Each type emits one durable event per item/due date, shared by matching workflows. Repeated and concurrent scans do not duplicate events. A due-date change can produce a new event. A missed reached day is covered by an overdue rule; the scanner does not backdate events or replay days. Enabling an overdue rule can observe currently overdue work at its next scan. Notification reminders keep their separate per-assignee rules.

## Ordered actions

Actions run in array order, up to 20 per version, with unique, non-empty UUID `id` values. They have `type` and only the parameters needed for that type:

| Type | Parameters and result |
| --- | --- |
| `Assign` | `userId`: active authorized project recipient. |
| `Unassign` | No parameters. |
| `ChangeStatus` | `value`: active status name in the execution's project. |
| `ChangePriority` | `value`: one of the four priorities. |
| `AddLabel`, `RemoveLabel` | `value`: existing label name in the execution's project. |
| `AddComment` | `value`: 1–10000 characters; uses existing server-validated `@{user-uuid}` mentions, checked at configuration/enablement and again at execution. |
| `Notify` | `userId`: creates an in-app `Workflow` notification, with no automatic email. |
| `SendEmail` | `userId`: queues a separate generic notification email if SMTP is configured; missing configuration is a permanent, visible action failure. |

Workspace rules resolve status/label names independently in each execution project. Configuration requires a matching target in at least one active scoped project; execution fails safely if that target does not exist in the actual project. Missing targets and invalid current permissions never receive endless retries.

Item mutations use the same member/status/label/mention rules as HTTP writes, update optimistic versions, and atomically persist action results, audit records and new domain events. Repeated assign/status/priority/label actions that already match are successful no-ops. Completed actions are never repeated by execution retries. Explicit workflow notifications/emails may target the creator; normal assignment/mention self-notification suppression is unchanged.

`SendEmail` success means its durable delivery job was committed. Execution details expose that job's `Pending`, `Sent`, `Cancelled`, or `Failed` status and safe error separately. Delivery rechecks the recipient's current email/project access, creator's current write authority, and item/project archive state. Bodies contain a generic sign-in prompt without private project data. Already committed jobs may finish after workflow disable; removed access cancels delivery. Existing SMTP ambiguity still permits a repeated inbox delivery after provider acceptance followed by a process failure.

## Durability, recovery, and observability

Domain-event persistence is the common reliable handoff. Notifications retain their existing outbox ownership; workflow scheduling has an independent `domain_events.workflow_processed_at` checkpoint. Scheduling and its executions/action ledgers commit together. Notification acknowledgement cannot consume a workflow event. Existing events are checkpointed by migration, preserving pending notification outbox work without historical workflow execution.

The execution worker uses expiring two-minute claims with PostgreSQL `FOR UPDATE SKIP LOCKED`, ownership checks, workspace serialization and a transaction per action. External SMTP runs in the existing email worker, outside workflow/workspace transactions. Crashes resume after the lease expires. Recovering an exhausted claim never increments the attempt counter beyond its configured limit; a committed final action can still be acknowledged without repeating it. Action side effects and their completion ledgers commit together; rolled-back effects are not published. Transient failures retry with bounded backoff (5, 10, 20, 40 seconds by default), up to five attempts. Validation/configuration failures stop immediately. Job cycles have 60-second timeouts and independent scheduling/execution/date loops.

States are `Pending`, `Running`, `Succeeded`, `Failed`, `Cancelled`. Nonmatching conditions finish `Succeeded` with `conditionsMatched: false`; their unexecuted actions are marked Cancelled with a reason. A failed action is Failed, later actions are Cancelled, and earlier successes remain. Safe errors omit provider messages and secrets.

Generated events carry correlation, origin execution, and incremented depth. Scheduling follows server-owned execution ancestry and blocks a workflow already in that branch. A configurable depth ceiling also bounds multi-rule chains; reused client trace IDs cannot suppress independent events. Unique `(workflow_id, event_id)` prevents duplicate scheduling.

Configuration changes and outcomes are audited. Item activity exposes `workflowExecutionId`; action records retain safe before/after values. The existing authorized `ProjectUpdated` SignalR contract publishes `WorkflowExecutionUpdated` with the execution ID after commit, and `NotificationReceived` carries committed notification IDs. Consumers refetch REST history and deduplicate event IDs as in Phase 4; no workflow definitions or private comment bodies are sent over the hub.

## Configuration and migrations

| Host setting | Default | Range |
| --- | --- | --- |
| `Workflows:Enabled` | `true` | Enables the three workflow job loops; APIs remain available. |
| `Workflows:PollSeconds` | `5` | 1–300 seconds. |
| `Workflows:DateScanIntervalMinutes` | `5` | 1–1440 minutes. |
| `Workflows:BatchSize` | `50` | 1–500. |
| `Workflows:MaxAttempts` | `5` | 1–10. |
| `Workflows:MaxDepth` | `8` | 1–20; events at the limit cannot schedule more rules. |

Compose exposes the first three through `WORKFLOWS_ENABLED`, `WORKFLOWS_POLL_SECONDS`, and `WORKFLOWS_DATE_SCAN_INTERVAL_MINUTES`. Host environment variables use double underscores. In-app notification live delivery and queued workflow email processing use the existing notification worker, so keep `Notifications:Enabled=true` for those deliveries.

`PhaseFiveWorkflows` adds workflow/version/execution/action tables, the workflow event checkpoint, calendar deduplication fields, workflow email targets and activity execution links. Database constraints enforce unique executions/action results, valid states and scoped references. A database trigger forbids configuration-version updates/deletions. Downgrade refuses to discard workflow history once any workflows exist; restore a pre-upgrade backup instead.

Run tests with an isolated `FLOWFORGE_TEST_CONNECTION`, using `dotnet test FlowForge.sln --filter 'FullyQualifiedName~PhaseFive'` from `backend/`, then run the full suite. No production credentials, real SMTP provider, Redis, or new external service is required.

The latest task-by-task completion review and regression evidence are in [workflow_engine_audit.md](workflow_engine_audit.md).
