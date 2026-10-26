# Phase 6: project dashboard backend

The matching React dashboard is now implemented separately; see [Phase 6 frontend](phase6_frontend.md) for UI scope and verification. The backend-only slice described below remains unchanged.

This slice delivers only the basic project dashboard backend/API. AI task breakdown, AI summaries and provider integrations are excluded. Milestones, project documents, velocity, burndown, cycle time and lead time are deferred. No frontend changes are included.

## Metric definitions

All metrics are calculated from committed PostgreSQL work items, never stored as editable counters or percentages. Tasks, issues and subtasks each count as one item; parent items do not roll up child estimates or counts. Labels and dependencies do not multiply counts. Archived work items are excluded from counts, completion, overdue and workload.

| Metric | Definition |
| --- | --- |
| Total items | Number of non-archived work items in this workspace/project. |
| Completed items | Included items whose status has category `Done`, regardless of status name or active flag. |
| Completion percentage | `completedItems * 100 / totalItems`, rounded to two decimal places with midpoint away from zero. Empty projects return `0`. |
| Counts by status | Included item counts per status ID, in status sort order then ID. Active statuses with no items return zero; inactive statuses appear only while they have included items. |
| Counts by priority | Included item counts in `Low`, `Medium`, `High`, `Critical` order, including zero buckets. |
| Overdue items | Included, unfinished items with a non-null due date strictly before the current UTC calendar date. Today is not overdue. Unassigned items are included. |
| Assignee workload | Unfinished, non-archived item count and summed estimate minutes, grouped by assignee user ID. Null estimates contribute zero. Sums use 64-bit integers. |
| Unassigned workload | A group with `assigneeUserId: null` and `displayName: "Unassigned"`, present when unassigned unfinished items exist. Users with no unfinished work are omitted; an empty project has no workload groups. |
| Recent activity | The ten most recent project activity records ordered by UTC occurrence time descending, then ID descending. This is audit history: records about archived work items remain visible. |

Reporting retains actual assignment attribution. If an assignee loses project/workspace access or becomes inactive while an item still references their retained membership, their workload stays attributed to their user ID. Reads do not reassign items. A workspace-member removal that clears an assignment through the existing write policy moves that work into Unassigned.

An archived project remains readable under its existing read permissions. Its non-archived work items still contribute to these metrics, including overdue and workload. This differs from overdue notification delivery, which skips archived projects and unassigned recipients.

Each dashboard response uses a PostgreSQL repeatable-read transaction so its totals and rows describe one database snapshot. Separate requests can observe later commits. The overview captures the clock once and exposes `asOf` and `todayUtc`; the overdue list captures its own UTC date once per request. No new table, migration, background job, cache or external service is required.

## Authorization and API

All routes require Bearer authentication. Owners/Admins can read every project in their workspace. Managers, Developers and Viewers require current explicit project membership. Removed/inactive users, outsiders, mismatched workspace/project pairs and unavailable projects receive `404`; anonymous callers receive `401`. Queries are scoped by both workspace and project. Reads do not create activity or domain events.

Base: `/api/v1/workspaces/{workspaceId}/projects/{projectId}/dashboard`.

| Method | Suffix | Response |
| --- | --- | --- |
| GET | `/` | `ProjectDashboardDto`: project ID, `asOf`, `todayUtc`, totals, percentage, overdue count, status/priority breakdowns and up to ten `recentActivity` records. |
| GET | `/workload?page=1&pageSize=20` | `PageDto<AssigneeWorkloadDto>`: `assigneeUserId`, `displayName`, `openItems`, `estimateMinutes`. Order: open item count descending, then user ID ascending (null last on PostgreSQL). `total` counts groups, not work items. |
| GET | `/overdue?page=1&pageSize=20` | `PageDto<OverdueWorkItemDto>`: `id`, readable `key`, `title`, `type`, `status`, `priority`, nullable `assigneeUserId`, `dueDate`, nullable `estimateMinutes`. Order: earliest due date, then item number ascending. `total` counts overdue items. |

The overview accepts the base path with or without a trailing slash; OpenAPI uses the path without it. Pagination uses the existing shared validator: page 1–1,000,000, page size 1–100; defaults 1 and 20. Out-of-range pages return an empty `items` array with the actual total. Invalid ranges return field-specific `400 ValidationProblem`; malformed integer query values return `400` through HTTP binding. No caller-supplied user IDs, sort expressions, date overrides or arbitrary filters are accepted by these routes.

Example overview for an empty project (status list and activity abbreviated):

```json
{
  "projectId": "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
  "asOf": "2026-10-22T00:00:00+00:00",
  "todayUtc": "2026-10-22",
  "totalItems": 0,
  "completedItems": 0,
  "completionPercentage": 0,
  "overdueItems": 0,
  "countsByStatus": [
    { "statusId": "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb", "name": "Todo", "category": "Open", "count": 0 }
  ],
  "countsByPriority": [
    { "priority": "Low", "count": 0 },
    { "priority": "Medium", "count": 0 },
    { "priority": "High", "count": 0 },
    { "priority": "Critical", "count": 0 }
  ],
  "recentActivity": []
}
```

The real overview includes all active project statuses. Activity uses the existing `ActivityDto`, including authorized before/after values, actor and workflow execution link. For older activity, use the existing paginated `GET /api/v1/workspaces/{workspaceId}/activity?projectId={projectId}`. No separate dashboard activity implementation is needed. Current SignalR project events can trigger client refetches; this slice adds no new hub events.

## Verification

`PhaseSixIntegrationTests` exercises the actual HTTP pipeline and EF/PostgreSQL queries against disposable databases. Coverage includes empty/zero buckets, task/issue/subtask counts, archived exclusions, category-based completion, UTC overdue boundaries, unassigned work, nullable and large estimate sums, labels/dependencies, committed updates and rollback, project permissions, revoked/inactive access, cross-project/workspace isolation, archived-project reads, read-only behavior, retained assignment attribution, recent activity ordering, pagination and OpenAPI.

From `backend/`, with `FLOWFORGE_TEST_CONNECTION` pointing to an isolated PostgreSQL server allowed to create/drop test databases:

```sh
dotnet build FlowForge.sln --no-restore -m:1 /p:UseSharedCompilation=false
dotnet test FlowForge.sln --no-build -m:1 --filter 'FullyQualifiedName~PhaseSix'
dotnet test FlowForge.sln --no-build -m:1
```

Without that connection, PostgreSQL tests are skipped; a skipped run is not verification of this slice. See [backend test results](../backend/TEST_RESULTS.md) for recorded outcomes. The subsequent [AI backend](ai_backend.md) includes an Ollama Cloud adapter with API key/model left for user configuration; milestones/documents and advanced metrics remain deferred.
