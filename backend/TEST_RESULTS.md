# Backend test results

## Latest verification: configurable Ollama Cloud adapter — 2026-10-22 (+07)

The full Release backend suite passes **351/351 tests**, **0 failed, 0 skipped**, with .NET SDK 10.0.113 and disposable PostgreSQL 17. This adds 36 cases to the previous 315-case suite. The adapter has **30 HTTP transport cases**, and the AI host class now has **10 cases** (40 total adapter/host outcomes, including four existing host cases). Release build succeeds with zero warnings/errors; Compose validates with `.env.example` and both Ollama values empty.

The selected provider transport is Ollama Cloud. `OllamaCloudAiProvider` is registered by default, but **`OLLAMA_API_KEY` and `OLLAMA_MODEL` remain blank**, with no fallback/default model and no real key installed. Missing either value disables generation without an HTTP request. Configuration is supplied by the user; see [Ollama setup](../docs/ai_backend.md#configure-ollama-cloud-yourself).

Verified with in-memory HTTP handlers: official HTTPS endpoint, Bearer key and user-selected model binding, `stream: false`, separate system/data messages, task/summary completion extraction, omission of unsupported Cloud structured-output parameters/tools, malformed/incomplete envelopes, text/body limits including unknown Content-Length, status classification, bounded Retry-After, no automatic retries, transport failures and cancellation. A configured host test exercises DI → real Ollama adapter → existing API output validation without external network access. Existing PostgreSQL feature, acceptance, dashboard and workflow tests remain green. This does not verify real Cloud connectivity, key validity, account/model access or model output quality; no request was sent to Ollama Cloud.

Evidence: [ollama-results.json](test-results/phase6/ollama-results.json), generated from `/tmp/flowforge-ollama-results/backend-ollama.trx`, with source SHA-256, timestamps and all 40 adapter/host outcomes. No schema or dependency changes are needed. Prior AI/dashboard results below are historical runs before the Cloud adapter was added.

Reproduce from `backend/`, with `FLOWFORGE_TEST_CONNECTION` targeting isolated PostgreSQL with create/drop database permission:

```sh
dotnet build FlowForge.sln --configuration Release --no-restore -m:1 /p:UseSharedCompilation=false
dotnet test FlowForge.sln --configuration Release --no-restore -m:1 /p:UseSharedCompilation=false \
  --logger 'trx;LogFileName=backend-ollama.trx' --results-directory /tmp/flowforge-ollama-results
```

## Historical verification: Phase 6 AI extension backend — 2026-10-22 (+07)

The full Release backend suite passes **315/315 tests**, **0 failures and 0 skips**, using .NET SDK 10.0.113 and disposable PostgreSQL 17. This includes **62 new AI extension cases**, all 26 dashboard cases and the other 227 existing cases. Standalone Release build: **0 warnings, 0 errors**. Compose configuration validates with `.env.example`. No migration or new dependency is added.

The AI backend is **provider-independent**: no model/vendor/SDK, external AI endpoint or credentials are selected/configured. Only the test host injects a fake provider. The real default application continues to use `UnconfiguredAiProvider`; missing AI configuration is an expected `503`, not fabricated success. Milestones/documents, advanced metrics and frontend UI remain deferred. See [AI contracts and adapter integration](../docs/ai_backend.md).

| Category | Result |
| --- | --- |
| AI output/request rules | PASS: 23 cases covering strict JSON, duplicate/unknown properties, size limits, title/priority/description limits, dependency IDs/cycles, explicit acceptance and bounded narrative. |
| Startup/adapter boundary | PASS: 4 cases covering validated limits and the actual unconfigured default provider; no model/network adapter is registered. |
| HTTP/PostgreSQL integration | PASS: 35 cases covering preview without writes, edited/selected confirmation, transactional tasks/dependencies/audit/outbox, concurrent retries and counters, token tampering/expiry/scope, deferred-commit rollback, permissions/revocation, bounded/scoped context and server facts, unavailable provider, sanitized failures, throttling, timeout and client cancellation. |
| Existing backend regressions | PASS: all 253 prior dashboard/auth/workspace/work-item/notification/workflow cases. |
| Live default no-model API | PASS: 23 HTTP/state checks against a separate Release API/database verify OpenAPI, liveness/readiness, empty-summary fallback, expected unconfigured `503`, anonymous denial, unconfirmed acceptance rejection, no AI-created tasks and manual-task/dashboard availability. |
| Existing live workflow journey | PASS: 66 checks (47 HTTP + 19 state); 16 polling requests recorded separately. |

Evidence: [all AI automated outcomes and Release summary](test-results/phase6/ai-automated-results.json), [live unconfigured API checks](test-results/phase6/ai-live-unconfigured.json), [live workflow regression](test-results/phase6/ai-workflow-regression.json). Source TRX: `/tmp/flowforge-phase6-ai-results/backend-phase6-ai.trx`; its SHA-256 and timestamps are recorded in the summary. Temporary API/database resources are stopped after verification. No real AI service or SMTP provider is used. Real model quality/provider transport remains unverified until a user-supplied adapter is added.

Reproduce from `backend/`, with `FLOWFORGE_TEST_CONNECTION` targeting isolated PostgreSQL with database-create/drop permission:

```sh
dotnet build FlowForge.sln --configuration Release --no-restore -m:1 /p:UseSharedCompilation=false
dotnet test FlowForge.sln --configuration Release --no-restore -m:1 /p:UseSharedCompilation=false \
  --logger 'trx;LogFileName=backend-phase6-ai.trx' --results-directory /tmp/flowforge-phase6-ai-results
```

Add `--filter 'FullyQualifiedName~PhaseSixAi'` for the AI extension subset. PostgreSQL tests skip without the test connection; skipped runs do not verify this slice. The earlier dashboard results below predate this AI extension.

## Historical verification: Phase 6 basic dashboard backend — 2026-10-22 (+07)

The full backend suite was rerun in **Release** and passes **253/253 tests**, with **0 failures and 0 skips**, using .NET SDK 10.0.113 and an isolated disposable PostgreSQL 17 server. This includes **26 Phase 6 HTTP/integration cases** and all 227 existing cases. The standalone Release solution build passes with **zero warnings/errors**. The initial Debug run also passed all 253 cases.

The separate rebuilt Release API then passed the existing workflow smoke journey (**66/66 checks**, 47 HTTP checks and 19 state assertions; 13 polling requests recorded separately) and **56/56 dashboard live checks**. Dashboard live checks cover all three GET operations, OpenAPI, empty metrics, task creation/completion/archive effects, unassigned estimates, overdue rows, anonymous/outsider denial, authorized Viewer reads, pagination errors/out-of-range pages and archived-project reads. The API used a separate disposable database, actual hosted workers and empty SMTP credentials.

Latest retest evidence: [Release suite/build summary and outcomes](test-results/phase6/release-retest.json), [dashboard HTTP/state checks](test-results/phase6/live-retest.json) and [workflow HTTP/state checks](test-results/phase6/workflow-live-retest.json). Source TRX: `/tmp/flowforge-phase6-retest/backend-retest.trx`; its SHA-256 and timestamps are recorded in the Release summary. The temporary API and PostgreSQL container are stopped after verification. These checks complete the agreed basic dashboard backend scope; deferred AI/optional features remain excluded.

Scope: basic project dashboard backend/API only. AI, milestone/document management, advanced reporting metrics and frontend work are excluded by agreement. No schema migration or new external dependency is needed. See [dashboard contracts and metric definitions](../docs/dashboard.md).

| Verification | Result |
| --- | --- |
| Overview API | PASS: totals, status/priority zero buckets, completion rounding, empty projects, ten recent activity records and OpenAPI route. |
| Overdue API | PASS: unfinished/non-archived items, UTC date boundary, unassigned work, stable earliest-date/item-number ordering and pagination. |
| Workload API | PASS: unfinished counts, null estimates, 64-bit estimate sums, Unassigned, retained assignments, stable ordering and pagination. |
| Counts and lifecycle | PASS: tasks/issues/subtasks counted once; category-based completion including inactive statuses; labels/dependencies do not inflate counts; committed updates, archive/restore and rolled-back writes. |
| Authorization/isolation | PASS: Owner/Admin, assigned Manager/Developer/Viewer, unassigned roles, anonymous callers, outsiders, removed/inactive users, cross-project/workspace pairs and archived-project reads. |
| Activity/read behavior | PASS: bounded recent activity matches existing authorized feed, deterministic timestamp/ID order, before/after metadata; reads emit no activity/domain events. |
| Input validation | PASS: field-specific pagination range errors and HTTP binding failures for malformed integers. |
| Full backend regressions | PASS: 253 passed, 0 failed, 0 skipped. |

Initial Debug evidence: [automated-results.json](test-results/phase6/automated-results.json), generated from `/tmp/flowforge-phase6-results/backend-phase6.trx`, with source SHA-256, run timestamps and all 26 dashboard outcomes. Automated dashboard cases use real PostgreSQL and the ASP.NET Core TestServer HTTP pipeline; the separate live checks above use an external Release API process. Neither run is a browser, load or production deployment test. Existing SMTP tests continue to use test senders; this change adds no AI or SMTP integration.

The first focused run exposed an EF translation failure when ordering a grouped workload after constructing a positional DTO. Ordering/paging now operates on an anonymous SQL projection before the DTO is constructed. The next run passed 25/26 cases; the remaining test incorrectly assumed an empty project had only its creation activity, overlooking the existing member-added audit record. That assertion was corrected. The final full run above passed all 26 dashboard cases.

To reproduce from `backend/`, set `FLOWFORGE_TEST_CONNECTION` to an isolated PostgreSQL server with permission to create/drop test databases:

```sh
dotnet build FlowForge.sln --configuration Release --no-restore -m:1 /p:UseSharedCompilation=false
dotnet test FlowForge.sln --configuration Release --no-restore -m:1 /p:UseSharedCompilation=false \
  --logger 'trx;LogFileName=backend-retest.trx' --results-directory /tmp/flowforge-phase6-retest
```

Add `--filter 'FullyQualifiedName~PhaseSix'` for the dashboard subset. Without the test connection, PostgreSQL-dependent cases are skipped and do not establish dashboard correctness. The older Phase 5 and Phase 1–4 results below are historical evidence for their respective runs.

Phase 5: [Endpoint matrix](#phase-5-rest-endpoint-matrix) · [Automated cases](#phase-5-automated-cases-by-category) · [Live HTTP results](#phase-5-live-release-api-individual-http-responses) · [Evidence and reproduction](#phase-5-evidence-and-reproduction).

## Historical verification: Phase 5 backend — 2026-10-18 (+07)

The latest Phase 5 endpoint/integration re-verification passes **210/210 tests**, with **zero failures and zero skips**, using .NET SDK 10.0.112 and disposable PostgreSQL 17. This includes the existing 143 cases and **67 Phase 5 cases**. The audit corrected exhausted-claim attempt counting, pre-enable comment mention validation and culture-dependent ISO dates, and exposed pinned definitions in execution history. See [the task-by-task completion audit](../docs/workflow_engine_audit.md).

The current Release API passed **66 live checks** against a separate disposable database: **47 HTTP response checks and 19 state assertions**. All **9/9 workflow OpenAPI operations** succeeded over real HTTP. Nine additional polling requests are recorded separately. This replaces the earlier 40-check smoke run as the latest live evidence. No application database or real SMTP provider was used.

| Verification | Result |
| --- | --- |
| Debug build | PASS; zero warnings/errors. |
| Full backend suite | PASS; 210/210, no skipped PostgreSQL cases. |
| Workflow condition/configuration rules | PASS; 31 cases. |
| Workflow startup option validation | PASS; 5 cases. |
| Workflow HTTP/integration/reliability | PASS; 29 cases. |
| Workflow SignalR WebSocket journey | PASS; 1 case. |
| Phase 4-to-5 migration and constraints | PASS; 1 case, including immutable versions, duplicate versions, trigger allowlist, legacy checkpoints and downgrade protection. |
| EF Core model consistency | PASS; no pending model changes. |
| Release publish | PASS. |
| Docker Compose configuration | PASS, using `.env.example`. |
| Current Release API HTTP/state journey | PASS; 66 checks (47 HTTP responses + 19 state assertions); all 9 workflow operations. |

The audit's new regression run reproduced **4 failures** before fixes: an exhausted claim reported 6 attempts rather than the configured 5, invalid comment mentions passed configuration validation, and two non-Gregorian cultures broke ISO date handling. All four now pass. Additional HTTP assertions verify historical pinned definitions and state-version errors; recovery tests exercise actual cancellation and a final action committed before a process crash. API DTOs are split into six files without renaming existing contract fields.

Workflow integration coverage exercises all seven triggers (including assignment/unassignment and both calendar triggers), all nine ordered action types, ALL/current-state conditions, version pinning, stale updates, enable/disable and disabled-event non-replay, management permissions, cross-workspace isolation, target revalidation, archived resources, notification and email authorization, separate notification/workflow consumption, rollback isolation, concurrent schedulers/runners, expired/exhausted claims, bounded transient retries, permanent failures, safe errors, completed-action preservation, poison event handling, depth limits, multi-workflow loops and reused HTTP trace IDs. The hosted-worker test runs registered scheduling/execution/date services. The WebSocket test verifies committed workflow notification IDs and execution outcome events against REST state.

The first focused run passed 45/48 cases and reproduced three failures from one issue: EF Core wrapped an injected transient storage error in `DbUpdateException`, so workflow retries treated it as permanent. The runner now classifies the inner exception; retry, disable/revocation, attempt-limit and full-suite regressions pass.

The live Release journey registers an owner, creates a workspace/project, validates/creates/reads/lists/enables/edits/disables/re-enables a workflow, triggers real background execution, verifies four ordered actions and optimistic versions, reads comments and linked activity, receives an in-app workflow notification, checks pinned configuration versions and disabled-event non-replay, and exercises authentication, invalid configuration/pagination, missing resources and stale-version responses.

Latest evidence: [automated results](test-results/phase5/automated-results.json) and [live HTTP/state results](test-results/phase5/live-http.json). Source TRX: `/tmp/flowforge-phase5-dotnet-results/backend-phase5-dotnet.trx`; Release output: `/tmp/flowforge-phase5-dotnet-publish`. The detailed category, endpoint, individual test-case and live-result tables below are generated from these actual outcomes.

Limits: Phase 5 is backend/API only. The workflow builder/frontend remains deferred. SMTP is exercised through the existing in-memory sender and failure simulation; real Gmail delivery remains unverified. SignalR uses actual WebSocket framing through TestServer; this is not a deployed browser or multi-instance backplane/load test. Workflow email action success means durable queueing; delivery status is reported separately. The existing SMTP ambiguity after provider acceptance remains. Workflow migration downgrade refuses to discard workflow history; use a pre-upgrade backup once workflows exist.

### Phase 5 test scope and counting

The current code was rebuilt and retested on 2026-10-18 (+07). The automated run finished at **2026-10-18T10:17:45+07:00**; the live Release journey started at **2026-10-18T10:17:55+07:00**. The API ran in Development mode on localhost to expose its OpenAPI contract, with real hosted workflow/notification workers and a separate disposable PostgreSQL 17 database. SMTP credentials were empty.

The **67 Phase 5 automated cases** comprise 31 rules, 5 startup validation, 29 API/integration/reliability, 1 SignalR WebSocket and 1 migration case. API endpoint assertions are part of those integration cases; the endpoint matrix is a mapping of the same coverage, not nine extra automated cases. The full backend result is **210 passed, 0 failed, 0 skipped**.

The separate live run has **66 checks: 47 HTTP response checks and 19 business-state assertions**. Another **9 polling HTTP requests** waited for eventual workflow/notification state. There were 56 actual HTTP requests in total; polling is recorded in JSON but is not counted again as business-state checks.

### Phase 5 REST endpoint matrix

All **9/9 workflow method/path operations** in the running API's OpenAPI document completed a successful live HTTP journey. The status sets below come from the recorded live requests; additional validation/permission paths appear in the automated integration case table. Base `W` is `/api/v1/workspaces/{workspaceId}/workflows`.

| Method | Route | Expected live statuses | Actual live statuses | Result |
| --- | --- | --- | --- | --- |
| GET | `W` | 200, 401 | 200, 401 | PASS |
| POST | `W` | 201, 403, 409 | 201, 403, 409 | PASS |
| POST | `W/validate` | 200, 400 | 200, 400 | PASS |
| GET | `W/{workflowId}` | 200, 404 | 200, 404 | PASS |
| PUT | `W/{workflowId}` | 200, 400, 409 | 200, 400, 409 | PASS |
| POST | `W/{workflowId}/disable` | 200, 400 | 200, 400 | PASS |
| POST | `W/{workflowId}/enable` | 200, 400, 409 | 200, 400, 409 | PASS |
| GET | `W/{workflowId}/executions` | 200, 400, 404 | 200, 400, 404 | PASS |
| GET | `W/{workflowId}/executions/{executionId}` | 200, 404 | 200, 404 | PASS |

A `400`, `401`, `403`, `404` or `409` in these tables is an expected rejection, not a failed test. The live runner checks the nine successful operations against the running OpenAPI document. It also verifies invalid triggers/mentions/state versions, developer management denial, outsider resource hiding, stale edits/enablement, history pagination, missing IDs and archived-project writes.

### Phase 5 automated cases by category

The following rows are taken from the final TRX outcome records. Parameterized rows are listed separately, matching the test runner's count. Expected outcome is PASS for each case; assertions inside each test define the HTTP/state/error expectations.

#### Unit: condition/configuration rules — 31 passed

Source: [PhaseFiveRulesTests.cs](tests/FlowForge.Api.Tests/PhaseFiveRulesTests.cs).

| Test case | Expected | Actual |
| --- | --- | --- |
| `Conditions_use_typed_comparisons(field: "Actor", op: "Equals", value: "11111111-1111-1111-1111-111111111111", expected: True)` | PASS | PASS |
| `Conditions_use_typed_comparisons(field: "DueDate", op: "Equals", value: "2026-10-18", expected: True)` | PASS | PASS |
| `Conditions_use_typed_comparisons(field: "DueDate", op: "GreaterThan", value: "2026-10-19", expected: False)` | PASS | PASS |
| `Conditions_use_typed_comparisons(field: "DueDate", op: "LessThan", value: "2026-10-19", expected: True)` | PASS | PASS |
| `Conditions_use_typed_comparisons(field: "Label", op: "Equals", value: "urgent", expected: True)` | PASS | PASS |
| `Conditions_use_typed_comparisons(field: "Label", op: "NotEquals", value: "bug", expected: False)` | PASS | PASS |
| `Conditions_use_typed_comparisons(field: "Priority", op: "Equals", value: "High", expected: True)` | PASS | PASS |
| `Conditions_use_typed_comparisons(field: "Priority", op: "GreaterThan", value: "Medium", expected: True)` | PASS | PASS |
| `Conditions_use_typed_comparisons(field: "Priority", op: "GreaterThanOrEqual", value: "High", expected: True)` | PASS | PASS |
| `Conditions_use_typed_comparisons(field: "Priority", op: "LessThan", value: "Critical", expected: True)` | PASS | PASS |
| `Conditions_use_typed_comparisons(field: "Priority", op: "LessThanOrEqual", value: "Medium", expected: False)` | PASS | PASS |
| `Conditions_use_typed_comparisons(field: "Priority", op: "NotEquals", value: "High", expected: False)` | PASS | PASS |
| `Conditions_use_typed_comparisons(field: "Status", op: "Equals", value: "Todo", expected: True)` | PASS | PASS |
| `Conditions_use_typed_comparisons(field: "Title", op: "Equals", value: "Example", expected: True)` | PASS | PASS |
| `Conditions_use_typed_comparisons(field: "Type", op: "Equals", value: "Issue", expected: False)` | PASS | PASS |
| `Empty_conditions_handle_nullable_and_collection_fields(field: "Assignee", op: "IsEmpty", expected: True)` | PASS | PASS |
| `Empty_conditions_handle_nullable_and_collection_fields(field: "Assignee", op: "IsNotEmpty", expected: False)` | PASS | PASS |
| `Empty_conditions_handle_nullable_and_collection_fields(field: "Description", op: "IsEmpty", expected: True)` | PASS | PASS |
| `Empty_conditions_handle_nullable_and_collection_fields(field: "Label", op: "IsEmpty", expected: False)` | PASS | PASS |
| `In_matches_any_allowlisted_value` | PASS | PASS |
| `Invalid_condition_configurations_are_rejected(field: "Actor", op: "Equals", value: "not-a-user")` | PASS | PASS |
| `Invalid_condition_configurations_are_rejected(field: "Assignee", op: "Equals", value: "00000000-0000-0000-0000-000000000000")` | PASS | PASS |
| `Invalid_condition_configurations_are_rejected(field: "DueDate", op: "Equals", value: "tomorrow")` | PASS | PASS |
| `Invalid_condition_configurations_are_rejected(field: "Priority", op: "Execute", value: "High")` | PASS | PASS |
| `Invalid_condition_configurations_are_rejected(field: "Priority", op: "GreaterThan", value: "Urgent")` | PASS | PASS |
| `Invalid_condition_configurations_are_rejected(field: "Script", op: "Equals", value: "code")` | PASS | PASS |
| `Invalid_condition_configurations_are_rejected(field: "Status", op: "GreaterThan", value: "Todo")` | PASS | PASS |
| `Invalid_condition_configurations_are_rejected(field: "Type", op: "Equals", value: "Epic")` | PASS | PASS |
| `Iso_date_conditions_use_gregorian_dates_independently_of_host_culture(culture: "ar-SA")` | PASS | PASS |
| `Iso_date_conditions_use_gregorian_dates_independently_of_host_culture(culture: "th-TH")` | PASS | PASS |
| `Null_collections_duplicate_action_ids_and_unexpected_parameters_are_rejected` | PASS | PASS |

#### Host: startup validation — 5 passed

Source: [PhaseFiveHostTests.cs](tests/FlowForge.Api.Tests/PhaseFiveHostTests.cs).

| Test case | Expected | Actual |
| --- | --- | --- |
| `Invalid_workflow_job_configuration_prevents_startup(setting: "BatchSize")` | PASS | PASS |
| `Invalid_workflow_job_configuration_prevents_startup(setting: "DateScanIntervalMinutes")` | PASS | PASS |
| `Invalid_workflow_job_configuration_prevents_startup(setting: "MaxAttempts")` | PASS | PASS |
| `Invalid_workflow_job_configuration_prevents_startup(setting: "MaxDepth")` | PASS | PASS |
| `Invalid_workflow_job_configuration_prevents_startup(setting: "PollSeconds")` | PASS | PASS |

#### API, endpoint, integration and reliability — 29 passed

Source: [PhaseFiveIntegrationTests.cs](tests/FlowForge.Api.Tests/PhaseFiveIntegrationTests.cs).

| Test case | Expected | Actual |
| --- | --- | --- |
| `All_ordered_actions_preserve_versions_audit_mentions_notifications_and_email` | PASS | PASS |
| `Api_events_schedule_and_execute_matching_rules(trigger: "CommentAdded")` | PASS | PASS |
| `Api_events_schedule_and_execute_matching_rules(trigger: "WorkItemAssignmentChanged")` | PASS | PASS |
| `Api_events_schedule_and_execute_matching_rules(trigger: "WorkItemCreated")` | PASS | PASS |
| `Api_events_schedule_and_execute_matching_rules(trigger: "WorkItemStatusChanged")` | PASS | PASS |
| `Api_events_schedule_and_execute_matching_rules(trigger: "WorkItemUpdated")` | PASS | PASS |
| `Assignment_and_unassignment_events_remain_independent_when_http_trace_ids_are_reused` | PASS | PASS |
| `Cancelled_worker_attempt_recovers_after_lease_without_repeating_committed_actions` | PASS | PASS |
| `Comment_action_mentions_are_validated_before_creation_and_enablement` | PASS | PASS |
| `Concurrent_schedulers_and_runners_deduplicate_and_break_multi_workflow_loops` | PASS | PASS |
| `Conditions_read_current_state_once_and_nonmatches_preserve_items` | PASS | PASS |
| `Date_triggers_include_unassigned_work_and_deduplicate_without_notification_side_effects` | PASS | PASS |
| `Depth_limits_and_poison_events_do_not_starve_later_valid_events` | PASS | PASS |
| `Disable_or_creator_revocation_cancels_unfinished_actions(revoke: False)` | PASS | PASS |
| `Disable_or_creator_revocation_cancels_unfinished_actions(revoke: True)` | PASS | PASS |
| `Enabling_revalidates_targets_and_cross_workspace_scope_cannot_be_referenced` | PASS | PASS |
| `Exhausted_claim_with_committed_last_action_finishes_without_repeating_it` | PASS | PASS |
| `Expired_claims_recover_and_exhausted_claims_finish_without_duplicate_effects` | PASS | PASS |
| `Hosted_worker_schedules_executes_and_scans_dates_through_registered_services` | PASS | PASS |
| `Management_permissions_validation_scope_and_archived_rules_follow_existing_policies` | PASS | PASS |
| `Notifications_cannot_consume_workflow_events_and_rollback_leaves_no_scheduled_execution` | PASS | PASS |
| `Pre_enable_and_disabled_events_are_not_replayed` | PASS | PASS |
| `Queued_workflow_email_rechecks_recipient_access_and_missing_smtp_is_diagnosable` | PASS | PASS |
| `Retry_resumes_after_committed_actions_and_terminal_errors_are_safe(permanent: False)` | PASS | PASS |
| `Retry_resumes_after_committed_actions_and_terminal_errors_are_safe(permanent: True)` | PASS | PASS |
| `Scheduled_work_cancels_when_item_or_project_is_archived(project: False)` | PASS | PASS |
| `Scheduled_work_cancels_when_item_or_project_is_archived(project: True)` | PASS | PASS |
| `Scheduling_pins_immutable_versions_and_rejects_stale_updates` | PASS | PASS |
| `Transient_retries_stop_at_limit_and_never_repeat_completed_actions` | PASS | PASS |

#### SignalR WebSocket integration — 1 passed

Source: [PhaseFiveRealtimeTests.cs](tests/FlowForge.Api.Tests/PhaseFiveRealtimeTests.cs).

| Test case | Expected | Actual |
| --- | --- | --- |
| `Workflow_notifications_and_execution_events_reach_authorized_websocket_clients_after_commit` | PASS | PASS |

#### Migration, constraints and upgrade — 1 passed

Source: [PhaseFiveMigrationTests.cs](tests/FlowForge.Api.Tests/PhaseFiveMigrationTests.cs).

| Test case | Expected | Actual |
| --- | --- | --- |
| `Phase_four_upgrade_preserves_notification_outbox_and_checkpoints_history_without_replay` | PASS | PASS |

### Phase 5 live Release API: individual HTTP responses

These 47 recorded HTTP response checks ran against the published Release binary through TCP, separately from TestServer. Polling requests remain available in the JSON ledger.

| Check | Method | Path | Expected | Actual | Result |
| --- | --- | --- | --- | --- | --- |
| API readiness | GET | `/health/ready` | 200 | 200 | PASS |
| OpenAPI workflow routes | GET | `/openapi/v1.json` | 200 | 200 | PASS |
| Register owner | POST | `/api/v1/auth/register` | 201 | 201 | PASS |
| Register developer | POST | `/api/v1/auth/register` | 201 | 201 | PASS |
| Read developer account | GET | `/api/v1/auth/me` | 200 | 200 | PASS |
| Register outsider | POST | `/api/v1/auth/register` | 201 | 201 | PASS |
| Read outsider account | GET | `/api/v1/auth/me` | 200 | 200 | PASS |
| Read signed-in account | GET | `/api/v1/auth/me` | 200 | 200 | PASS |
| Create workspace | POST | `/api/v1/workspaces` | 201 | 201 | PASS |
| Create project | POST | `/api/v1/workspaces/{workspaceId}/projects` | 201 | 201 | PASS |
| Add workspace developer | POST | `/api/v1/workspaces/{workspaceId}/members` | 201 | 201 | PASS |
| Validate workflow | POST | `/api/v1/workspaces/{workspaceId}/workflows/validate` | 200 | 200 | PASS |
| Reject invalid trigger | POST | `/api/v1/workspaces/{workspaceId}/workflows/validate` | 400 | 400 | PASS |
| Require authentication | GET | `/api/v1/workspaces/{workspaceId}/workflows` | 401 | 401 | PASS |
| Reject developer workflow management | POST | `/api/v1/workspaces/{workspaceId}/workflows` | 403 | 403 | PASS |
| Reject inaccessible comment mention before enablement | POST | `/api/v1/workspaces/{workspaceId}/workflows/validate` | 400 | 400 | PASS |
| Create workflow | POST | `/api/v1/workspaces/{workspaceId}/workflows` | 201 | 201 | PASS |
| Read workflow | GET | `/api/v1/workspaces/{workspaceId}/workflows/{workflowId}` | 200 | 200 | PASS |
| Hide workflow from outsider | GET | `/api/v1/workspaces/{workspaceId}/workflows/{workflowId}` | 404 | 404 | PASS |
| Reject invalid state version | POST | `/api/v1/workspaces/{workspaceId}/workflows/{workflowId}/enable` | 400 | 400 | PASS |
| List workflows | GET | `/api/v1/workspaces/{workspaceId}/workflows` | 200 | 200 | PASS |
| Enable workflow | POST | `/api/v1/workspaces/{workspaceId}/workflows/{workflowId}/enable` | 200 | 200 | PASS |
| Create triggering work item | POST | `/api/v1/workspaces/{workspaceId}/projects/{projectId}/work-items` | 201 | 201 | PASS |
| Read execution history | GET | `/api/v1/workspaces/{workspaceId}/workflows/{workflowId}/executions` | 200 | 200 | PASS |
| Execution details | GET | `/api/v1/workspaces/{workspaceId}/workflows/{workflowId}/executions/{executionId}` | 200 | 200 | PASS |
| Hide execution history from developer | GET | `/api/v1/workspaces/{workspaceId}/workflows/{workflowId}/executions` | 404 | 404 | PASS |
| Read automated item | GET | `/api/v1/workspaces/{workspaceId}/projects/{projectId}/work-items/{workItemId}` | 200 | 200 | PASS |
| Read automated comment | GET | `/api/v1/workspaces/{workspaceId}/projects/{projectId}/work-items/{workItemId}/comments` | 200 | 200 | PASS |
| Read activity | GET | `/api/v1/workspaces/{workspaceId}/activity?projectId={projectId}&workItemId={workItemId}` | 200 | 200 | PASS |
| Update workflow creates version | PUT | `/api/v1/workspaces/{workspaceId}/workflows/{workflowId}` | 200 | 200 | PASS |
| Read historical execution after edit | GET | `/api/v1/workspaces/{workspaceId}/workflows/{workflowId}/executions/{executionId}` | 200 | 200 | PASS |
| Reject stale update | PUT | `/api/v1/workspaces/{workspaceId}/workflows/{workflowId}` | 409 | 409 | PASS |
| Reject stale enable | POST | `/api/v1/workspaces/{workspaceId}/workflows/{workflowId}/enable` | 409 | 409 | PASS |
| Disable workflow | POST | `/api/v1/workspaces/{workspaceId}/workflows/{workflowId}/disable` | 200 | 200 | PASS |
| Create item while disabled | POST | `/api/v1/workspaces/{workspaceId}/projects/{projectId}/work-items` | 201 | 201 | PASS |
| Reenable workflow | POST | `/api/v1/workspaces/{workspaceId}/workflows/{workflowId}/enable` | 200 | 200 | PASS |
| Create item after reenable | POST | `/api/v1/workspaces/{workspaceId}/projects/{projectId}/work-items` | 201 | 201 | PASS |
| Read execution history after new version | GET | `/api/v1/workspaces/{workspaceId}/workflows/{workflowId}/executions` | 200 | 200 | PASS |
| Reject invalid pagination | GET | `/api/v1/workspaces/{workspaceId}/workflows/{workflowId}/executions?pageSize=101` | 400 | 400 | PASS |
| Reject missing execution | GET | `/api/v1/workspaces/{workspaceId}/workflows/{workflowId}/executions/{executionId}` | 404 | 404 | PASS |
| Disable after audit | POST | `/api/v1/workspaces/{workspaceId}/workflows/{workflowId}/disable` | 200 | 200 | PASS |
| Reject invalid disable version | POST | `/api/v1/workspaces/{workspaceId}/workflows/{workflowId}/disable` | 400 | 400 | PASS |
| Reject invalid workflow update | PUT | `/api/v1/workspaces/{workspaceId}/workflows/{workflowId}` | 400 | 400 | PASS |
| Hide nonexistent workflow | GET | `/api/v1/workspaces/{workspaceId}/workflows/{workflowId}` | 404 | 404 | PASS |
| Archive project | POST | `/api/v1/workspaces/{workspaceId}/projects/{projectId}/archive` | 200 | 200 | PASS |
| Reject workflow enable on archived project | POST | `/api/v1/workspaces/{workspaceId}/workflows/{workflowId}/enable` | 409 | 409 | PASS |
| Reject workflow creation on archived project | POST | `/api/v1/workspaces/{workspaceId}/workflows` | 409 | 409 | PASS |

### Phase 5 live Release API: business-state assertions

| Check | Expected | Actual | Result |
| --- | --- | --- | --- |
| Workflow management appears in OpenAPI | true | true | PASS |
| Mention validation returns field errors | true | true | PASS |
| Created workflow starts disabled | true | true | PASS |
| State validation returns version field error | true | true | PASS |
| List contains created workflow | true | true | PASS |
| Hosted worker completes workflow | true | true | PASS |
| Execution exposes pinned version and definition | true | true | PASS |
| Execution exposes original actor and source metadata | true | true | PASS |
| All ordered actions succeeded | true | true | PASS |
| Automation preserves item mutations | true | true | PASS |
| Automated comment persisted once | true | true | PASS |
| Audit exposes execution linkage | true | true | PASS |
| Workflow notification persisted | true | true | PASS |
| Historical definition remains inspectable after edit | true | true | PASS |
| Configuration version increments | true | true | PASS |
| Disabled events are not replayed | true | true | PASS |
| Execution pins version | true | true | PASS |
| History returns both immutable definitions | true | true | PASS |
| All nine workflow OpenAPI operations succeed over HTTP | true | true | PASS |

### Phase 5 evidence and reproduction

Durable, sanitized evidence is saved in the repository:

- [Automated results](test-results/phase5/automated-results.json): the overall totals, all 67 Phase 5 outcomes, source file/line, durations, run timestamps and source TRX SHA-256.
- [Live HTTP/state results](test-results/phase5/live-http.json): all 66 checks plus every HTTP request, including polling. Routes use ID placeholders; no authentication tokens, passwords, request bodies or email addresses are stored.
- [.NET live HTTP runner](tests/FlowForge.Api.SmokeTests/Program.cs): reproduces the published API journey using `HttpClient`, without new NuGet packages. The solution builds it in CI; it runs explicitly against a disposable API.
- [Runner parity evidence](test-results/phase5/runner-parity.json): all 66 ordered check records exactly match the former runner, including names, methods, sanitized paths, expected/actual values and flags. A deliberate unexpected 404 also verified exit code 1 and persistence of the failed-check report.

The source TRX is `/tmp/flowforge-phase5-dotnet-results/backend-phase5-dotnet.trx`; the verified Release output is `/tmp/flowforge-phase5-dotnet-publish`. Build/publish succeeded; EF Core reports no pending model changes. The test container and temporary API are stopped after verification.

From the repository root, set `FLOWFORGE_TEST_CONNECTION` to an isolated PostgreSQL server, then run:

```sh
dotnet restore backend/FlowForge.sln -m:1
dotnet build backend/FlowForge.sln --no-restore -m:1 /p:UseSharedCompilation=false
dotnet test backend/FlowForge.sln --no-build --no-restore -m:1 \
  --logger 'trx;LogFileName=backend-phase5-dotnet.trx' \
  --results-directory /tmp/flowforge-phase5-dotnet-results
```

To run only Phase 5, add `--filter 'FullyQualifiedName~PhaseFive'`. For live HTTP, publish and start a disposable API/database with workflow and notification workers enabled and SMTP unset, then run:

```sh
dotnet run --project backend/tests/FlowForge.Api.SmokeTests --no-build --no-restore -- \
  --base-url http://127.0.0.1:5197 \
  --output backend/test-results/phase5/live-http.json
```

The .NET live runner creates disposable users/workspace/project/workflows and archives its project at the end. The runner preserves the original 200 ms polling cadence, 15-second execution deadline, 10-second notification deadline and 10-second HTTP timeout. Polling counts depend on worker timing.

The full suite's 210-case total and its 67-case Phase 5 subset are counted once. Real SMTP inbox delivery, deployed-browser/multi-instance behavior and load/security certification remain outside these checks.


The sections below retain the historical Phase 1–4 audits and their results; references there to unimplemented workflows describe the repository on 2026-10-17.

Historical full backend audit: 2026-10-17 (+07), .NET SDK 10.0.112 and disposable PostgreSQL 17. All **143 automated tests passed**, including **26 Phase 4 cases** and **7 system-audit regressions**, with zero failures/skips. A separately running Release API passed **50 live checks** (46 HTTP requests and four state assertions). No application database or real SMTP service was used. See the full backend audit section for the three reproduced and corrected defects.

Phase 4 covers notification APIs and permissions, assignment/self-notification rules, new/edited mentions, encrypted invitation email, cancellation after revocation/removal, overdue UTC boundaries and deduplication, concurrent dispatchers/scanners, expiring claims, retry limits, safe errors, rollback isolation, hosted-worker processing, Phase 3 migration upgrades, and real SignalR WebSocket handshake/subscription/event/reconnect journeys through TestServer. New regressions verify that slow SMTP does not block workspace writes or live dispatch, revoked access during an in-flight email cannot expose private resource data (including earlier queued bodies), a failed overdue scanner does not block outbox/email, and HTTP logs preserve method/path/status/duration without query tokens. Email uses the in-memory test sender; real Gmail inbox delivery remains unverified.

The earlier Phase 3 retest and live HTTP evidence below are retained as historical results. The exception-handling regression run was performed on 2026-10-16 20:13 +07.

## Historical Phase 1–4 result summary

| Run | Passed | Failed | Skipped | Total |
| --- | ---: | ---: | ---: | ---: |
| Automated suite before fix (historical) | 95 | 6 | 0 | 101 |
| Automated suite after fix | 101 | 0 | 0 | 101 |
| Live HTTP checks after fix, Development | 79 | 0 | 0 | 79 |
| Live HTTP checks after fix, Production | 8 | 0 | 0 | 8 |
| Phase 3 endpoint/integration and migration tests, latest | 9 | 0 | 0 | 9 |
| Full automated backend suite, Phase 3 historical | 110 | 0 | 0 | 110 |
| Phase 4 initial implementation, historical | 18 | 0 | 0 | 18 |
| Phase 4 after P2 fixes and simplification, historical | 21 | 0 | 0 | 21 |
| Phase 4 endpoint verification, historical | 24 | 0 | 0 | 24 |
| Phase 4 module input validation, latest | 26 | 0 | 0 | 26 |
| Full automated backend suite, initial Phase 4 historical | 128 | 0 | 0 | 128 |
| Full automated backend suite after P2 fixes, historical | 131 | 0 | 0 | 131 |
| Full automated backend suite before module validators, historical | 134 | 0 | 0 | 134 |
| Full automated backend suite before system audit, historical | 136 | 0 | 0 | 136 |
| Full automated backend suite after audit fixes, latest | 143 | 0 | 0 | 143 |
| System audit regressions before fixes | 0 | 6 | 0 | 6 |
| System audit regressions after fixes, including concurrent refresh | 7 | 0 | 0 | 7 |
| Release API live audit checks | 50 | 0 | 0 | 50 |
| Phase 3 live HTTP calls, Development, latest | 27 | 0 | 0 | 27 |

The latest build passed with zero warnings and zero errors. EF Core reported no pending model changes. All six previously failing cases still pass in the automated suite. The historical 101-case run included the original 82 cases and 19 added coverage cases.

The earlier live run exercised all **34 endpoint operations available at that time** over real HTTP: 31 business operations, two health endpoints, and Development OpenAPI. Phase 3's **26 route operations** were exercised through both the ASP.NET Core HTTP pipeline with `WebApplicationFactory` and a separately running Development API process against disposable PostgreSQL. The live journey made 27 calls because it created two work items. The earlier live operation list was checked against that running API's OpenAPI document. Endpoint exercise is not a claim of exhaustive input, security, or line coverage.

## Historical full backend audit (2026-10-17; Phases 1–4)

Scope: implemented backend Phases 1–4, identity/session handling, workspace/project access, work items and collaboration, files/audit, notifications/SignalR/background delivery, migrations, host configuration, CI/Compose configuration, and a Release publish. The frontend and future workflow/reporting/AI phases are not implemented.

### Reproduced defects and fixes

The original 136 tests all passed. Additional targeted tests then reproduced six failing cases across three defects; all were fixed before the final full run.

| Defect | Before fix | Correction and verified result |
| --- | --- | --- |
| Time-entry timestamps with non-UTC offsets | Valid `+07:00` and `-05:00` values caused `500` when Npgsql wrote `timestamptz` | Normalize `startedAt` with `ToUniversalTime()` on both POST and PUT; return `201`/`200` and preserve the same instant in UTC. Two regression cases passed. |
| Invalid JWT subject shape | Signed tokens with missing/malformed `sub` caused `500`; an empty GUID reached some authenticated endpoints | Validate the non-empty GUID subject in `OnTokenValidated`; all three invalid-subject variants return `401` for identity/workspace/notification endpoints and hub negotiation. |
| Logout racing with refresh | Logout could miss the uncommitted replacement session and leave its refresh token active | Serialize refresh/reuse/logout with a PostgreSQL transaction advisory lock per token family and re-read state after acquiring the lock. A deterministic PostgreSQL lock-wait test proves logout revokes the replacement; a concurrent double-refresh test proves reuse also revokes the winning replacement. |

No migration or new dependency was required. Existing API input validation, project authorization, and module boundaries were preserved. Startup/configuration checks, transaction/constraint tests, file authorization/cleanup tests, and the Phase 4 retry/concurrency/reconnect regressions remain green.

### Final verification

| Check | Result |
| --- | --- |
| Existing test baseline before new audit cases | PASS: 136/136 |
| Full automated suite after fixes | PASS: 143/143; zero failures/skips |
| New audit regressions | PASS: 7/7; six reproduced failures corrected plus one concurrent-refresh guard |
| Phase 4 cases within the full suite | PASS: 26/26 |
| Release publish | PASS: `dotnet publish ... -c Release --no-restore -o /tmp/flowforge-system-audit-publish` |
| EF Core pending model changes | PASS: no changes since the last migration |
| Fresh database migrations | PASS: all seven migrations applied by the fixture and Release API startup |
| Docker Compose configuration | PASS: `docker compose -f backend/compose.yaml --env-file backend/.env.example config --quiet` |
| Live Release API | PASS: 46 HTTP requests and four state assertions on localhost, with a separate disposable database and SMTP disabled |

TRX: `/tmp/flowforge-system-audit-results/system-final.trx`. Reproduced pre-fix failures: `/tmp/flowforge-system-audit-results/audit-before-fixes.trx`. Sanitized live results: `/tmp/flowforge-system-audit-results/live-http.json`. Temporary users, database, API process, and PostgreSQL container were created solely for this audit.

### Live Release API results

The published Release binary ran in Development mode on `127.0.0.1:5193` to expose its OpenAPI contract. Production-mode OpenAPI/readiness behavior is covered by the automated host tests. The following table lists actual live checks; status values such as `400`, `401`, `403`, `404`, and `409` are expected rejection cases.

| Check | Method | Expected | Actual | Result |
| --- | --- | --- | --- | --- |
| GET /health/live | GET | 200 | 200 | PASS |
| GET /health/ready | GET | 200 | 200 | PASS |
| GET /openapi/v1.json | GET | 200 | 200 | PASS |
| Register owner | POST | 201 | 201 | PASS |
| Register developer | POST | 201 | 201 | PASS |
| Register outsider | POST | 201 | 201 | PASS |
| GET /api/v1/auth/me | GET | 200 | 200 | PASS |
| GET /api/v1/auth/me | GET | 200 | 200 | PASS |
| POST /api/v1/workspaces | POST | 201 | 201 | PASS |
| GET /api/v1/workspaces/2bce3319-2548-47d5-8c5b-d09d7d03281a | GET | 200 | 200 | PASS |
| POST /api/v1/workspaces/2bce3319-2548-47d5-8c5b-d09d7d03281a/members | POST | 201 | 201 | PASS |
| POST /api/v1/workspaces/2bce3319-2548-47d5-8c5b-d09d7d03281a/projects | POST | 201 | 201 | PASS |
| POST /api/v1/workspaces/2bce3319-2548-47d5-8c5b-d09d7d03281a/projects/1e669914-b275-4030-a54e-07a1ada12371/members | POST | 201 | 201 | PASS |
| GET /api/v1/workspaces/2bce3319-2548-47d5-8c5b-d09d7d03281a/projects/1e669914-b275-4030-a54e-07a1ada12371/statuses | GET | 200 | 200 | PASS |
| POST /api/v1/workspaces/2bce3319-2548-47d5-8c5b-d09d7d03281a/projects/1e669914-b275-4030-a54e-07a1ada12371/work-items | POST | 201 | 201 | PASS |
| GET /api/v1/workspaces/2bce3319-2548-47d5-8c5b-d09d7d03281a/projects/1e669914-b275-4030-a54e-07a1ada12371/work-items/83b2c1ba-cfbe-4924-baae-8394d6b208e3 | GET | 200 | 200 | PASS |
| Outsider denied project item | GET | 404 | 404 | PASS |
| PUT /api/v1/workspaces/2bce3319-2548-47d5-8c5b-d09d7d03281a/projects/1e669914-b275-4030-a54e-07a1ada12371/work-items/83b2c1ba-cfbe-4924-baae-8394d6b208e3 | PUT | 200 | 200 | PASS |
| Stale work item version denied | PUT | 409 | 409 | PASS |
| POST /api/v1/workspaces/2bce3319-2548-47d5-8c5b-d09d7d03281a/projects/1e669914-b275-4030-a54e-07a1ada12371/work-items/83b2c1ba-cfbe-4924-baae-8394d6b208e3/time-entries | POST | 201 | 201 | PASS |
| Time entry +07 normalized to UTC | ASSERT | True | True | PASS |
| PUT /api/v1/workspaces/2bce3319-2548-47d5-8c5b-d09d7d03281a/projects/1e669914-b275-4030-a54e-07a1ada12371/work-items/83b2c1ba-cfbe-4924-baae-8394d6b208e3/time-entries/d23200f6-aa01-4a4d-8a86-2b15c5a1454e | PUT | 200 | 200 | PASS |
| Time entry -05 update normalized to UTC | ASSERT | True | True | PASS |
| GET /api/v1/workspaces/2bce3319-2548-47d5-8c5b-d09d7d03281a/projects/1e669914-b275-4030-a54e-07a1ada12371/work-items/83b2c1ba-cfbe-4924-baae-8394d6b208e3/time-entries | GET | 200 | 200 | PASS |
| POST /api/v1/workspaces/2bce3319-2548-47d5-8c5b-d09d7d03281a/projects/1e669914-b275-4030-a54e-07a1ada12371/work-items/83b2c1ba-cfbe-4924-baae-8394d6b208e3/comments | POST | 201 | 201 | PASS |
| GET /api/v1/workspaces/2bce3319-2548-47d5-8c5b-d09d7d03281a/projects/1e669914-b275-4030-a54e-07a1ada12371/work-items/83b2c1ba-cfbe-4924-baae-8394d6b208e3/comments | GET | 200 | 200 | PASS |
| POST /api/v1/workspaces/2bce3319-2548-47d5-8c5b-d09d7d03281a/projects/1e669914-b275-4030-a54e-07a1ada12371/work-items/83b2c1ba-cfbe-4924-baae-8394d6b208e3/attachments | POST | 201 | 201 | PASS |
| GET /api/v1/workspaces/2bce3319-2548-47d5-8c5b-d09d7d03281a/projects/1e669914-b275-4030-a54e-07a1ada12371/work-items/83b2c1ba-cfbe-4924-baae-8394d6b208e3/attachments/64f8bf56-f29a-48ac-9fb0-05106246c0a1 | GET | 200 | 200 | PASS |
| Outsider denied attachment | GET | 404 | 404 | PASS |
| DELETE /api/v1/workspaces/2bce3319-2548-47d5-8c5b-d09d7d03281a/projects/1e669914-b275-4030-a54e-07a1ada12371/work-items/83b2c1ba-cfbe-4924-baae-8394d6b208e3/attachments/64f8bf56-f29a-48ac-9fb0-05106246c0a1 | DELETE | 204 | 204 | PASS |
| GET /api/v1/workspaces/2bce3319-2548-47d5-8c5b-d09d7d03281a/activity?projectId=1e669914-b275-4030-a54e-07a1ada12371 | GET | 200 | 200 | PASS |
| Actual background worker delivered assignment and mention notifications | ASSERT | True | True | PASS |
| GET /api/v1/notifications?page=1&pageSize=1&unreadOnly=true | GET | 200 | 200 | PASS |
| POST /api/v1/notifications/e5b03fbc-2a95-4a32-b89c-041a08aacf8d/read | POST | 204 | 204 | PASS |
| Repeated mark-read is idempotent | POST | 204 | 204 | PASS |
| POST /api/v1/notifications/read-all | POST | 204 | 204 | PASS |
| GET /api/v1/notifications?unreadOnly=true | GET | 200 | 200 | PASS |
| Read-all cleared unread list | ASSERT | True | True | PASS |
| GET /api/v1/notifications?page=0&pageSize=101 | GET | 400 | 400 | PASS |
| POST /api/v1/notifications/00000000-0000-0000-0000-000000000000/read | POST | 400 | 400 | PASS |
| GET /api/v1/notifications | GET | 401 | 401 | PASS |
| POST /api/v1/workspaces/2bce3319-2548-47d5-8c5b-d09d7d03281a/invitations | POST | 201 | 201 | PASS |
| GET /api/v1/invitations/e33fa3a3-5183-4add-89da-53438628f496 | GET | 200 | 200 | PASS |
| Unverified invited account cannot accept | POST | 403 | 403 | PASS |
| POST /api/v1/workspaces/2bce3319-2548-47d5-8c5b-d09d7d03281a/invitations/e33fa3a3-5183-4add-89da-53438628f496/revoke | POST | 204 | 204 | PASS |
| Invalid JWT subject rejected: None | GET | 401 | 401 | PASS |
| Invalid JWT subject rejected: not-a-guid | GET | 401 | 401 | PASS |
| Invalid JWT subject rejected: 00000000-0000-0000-0000-000000000000 | GET | 401 | 401 | PASS |
| POST /api/v1/auth/logout | POST | 204 | 204 | PASS |
| Logged-out refresh token rejected | POST | 401 | 401 | PASS |

### Practical limits

Real Gmail/inbox delivery, browser UI, a reverse proxy, and multi-instance Redis backplane behavior were not exercised. No real email was sent. Tests passing and this source review do not establish that every possible input or operational failure is covered. No further failing case was found in the checks listed above.

## Phase 4 validation organization

Request input validation already existed. The shared validator folder was renamed from `FlowForge.Api/Validation` to `FlowForge.Api/Validations`, and namespaces/imports were updated in endpoints and tests. The original 26 validator files retain their validation logic. Two dedicated Phase 4 validators now handle empty notification and subscription IDs; pagination remains shared, with no pass-through list validator or new migration.

| Phase 4 input | Validation location | Verified result |
| --- | --- | --- |
| `page`, `pageSize` | `Validations/PaginationValidator.cs`, used directly by `NotificationEndpoints` | PASS: defaults and valid pagination; invalid bounds return `400` |
| Integer/boolean query formats | ASP.NET Core parameter binding | PASS: malformed `page` and `unreadOnly` return `400` |
| `notificationId` | `Validations/Notifications/NotificationRequestValidator.cs` | PASS: `Guid.Empty` returns field-specific `400 application/problem+json`; a non-empty missing ID still returns `404` |
| `workspaceId`, `projectId` in all join/leave methods | `Validations/Realtime/SubscriptionRequestValidator.cs` | PASS: empty IDs produce safe field-specific hub errors; a join with both empty IDs reports both fields |
| Malformed GUIDs or missing hub arguments | SignalR parameter binding | PASS: invalid invocations are rejected and the connection remains usable for valid calls |
| Notification ownership and subscription scope | Endpoint/service authorization and resource lookup | PASS: missing/inaccessible notification returns `404`; unauthorized hub subscriptions are rejected |

Background option ranges remain configuration validation in Infrastructure (`NotificationJobOptions` Data Annotations), with startup validation in the API host. Build passed with zero warnings/errors. The full rerun passed **136/136**, including **26/26 Phase 4** cases, with no skips; TRX: `/tmp/flowforge-module-validation-results/backend-module-validation.trx`.

## Phase 4 endpoint and SignalR verification

Verified on 2026-10-17 (+07) with the actual ASP.NET Core HTTP/SignalR pipeline in `WebApplicationFactory`/TestServer and disposable PostgreSQL 17. The hub tests exchange real WebSocket/SignalR JSON frames. These results are automated integration checks, separate from the historical external-process live HTTP tables below. All Phase 4 operations listed here were exercised; this does not claim exhaustive coverage of every possible input or transport.

### REST endpoints

The additional `Empty_notification_id_returns_field_validation_problem_before_resource_lookup` case verifies `POST /api/v1/notifications/{Guid.Empty}/read` returns `400` with an `errors.notificationId` entry; unknown non-empty IDs retain `404`.

| Method and route | Verified behavior | Observed result | Evidence |
| --- | --- | --- | --- |
| `GET /api/v1/notifications` | Own notifications only; valid page 1/2 and page size; total count; unread filtering; resource links; removed project access | PASS: `200` | `Assignment_mentions_read_apis_and_email_are_idempotent_and_authorized`; `Notification_endpoints_enforce_authentication_paging_and_read_all_recipient_isolation`; `Removed_members_cannot_read_or_receive_queued_project_notifications` |
| `GET /api/v1/notifications` | Missing authentication, query token without Bearer header, inactive user; invalid pagination and malformed page/unread flag | PASS: `401` for authentication failures; `400` for invalid queries | `Notification_endpoints_enforce_authentication_paging_and_read_all_recipient_isolation`; `Assignment_mentions_read_apis_and_email_are_idempotent_and_authorized` |
| `POST /api/v1/notifications/{notificationId}/read` | Own notification becomes read; repeat remains successful; another recipient's/missing/inaccessible notification is unavailable; anonymous call rejected | PASS: `204`, `404`, `401` respectively | `Assignment_mentions_read_apis_and_email_are_idempotent_and_authorized`; `Notification_endpoints_enforce_authentication_paging_and_read_all_recipient_isolation`; `Removed_members_cannot_read_or_receive_queued_project_notifications` |
| `POST /api/v1/notifications/read-all` | All visible own notifications become read; repeated call preserves read timestamps; another recipient's notifications remain unread | PASS: `204` | `Notification_endpoints_enforce_authentication_paging_and_read_all_recipient_isolation` |
| `POST /api/v1/notifications/read-all` | Missing authentication and inactive account are rejected | PASS: `401` | `Notification_endpoints_enforce_authentication_paging_and_read_all_recipient_isolation` |

### SignalR connection and hub methods

`Hub_validates_empty_ids_for_all_subscription_methods_and_binding_rejects_malformed_guids` verifies empty IDs in all four methods, malformed GUIDs, missing arguments, field-specific errors, and successful valid subscriptions afterward.

| Operation | Verified behavior | Result | Evidence |
| --- | --- | --- | --- |
| `POST /hubs/updates/negotiate?negotiateVersion=1` | Anonymous negotiation rejected | PASS: `401` | `Signalr_authorizes_subscriptions_delivers_compact_events_and_recovers_by_refetch_after_reconnect` |
| WebSocket `/hubs/updates` | JWT in Authorization header and hub query token; successful JSON protocol handshake; query token absent from captured logs | PASS | `Signalr_authorizes_subscriptions_delivers_compact_events_and_recovers_by_refetch_after_reconnect` |
| `JoinProject(workspaceId, projectId)` | Authorized join receives events; outsider, wrong workspace, inaccessible same-workspace project, and removed membership are rejected | PASS: completion on success; safe hub error on denial | `Leaving_project_and_workspace_stops_delivery_and_workspace_subscriptions_exclude_private_projects`; `Existing_signalr_connection_stops_receiving_project_events_after_access_revocation`; reconnect journey |
| `LeaveProject(projectId)` | Successful leave and repeated leave; no subsequent project update received; later rejoin works | PASS | `Leaving_project_and_workspace_stops_delivery_and_workspace_subscriptions_exclude_private_projects` |
| `JoinWorkspace(workspaceId)` | Member receives authorized project updates; outsider denied; private project without membership does not reach the workspace subscription | PASS | `Leaving_project_and_workspace_stops_delivery_and_workspace_subscriptions_exclude_private_projects`; reconnect journey |
| `LeaveWorkspace(workspaceId)` | Successful leave and repeated leave; no subsequent workspace event received; separate project subscription can be established afterward | PASS | `Leaving_project_and_workspace_stops_delivery_and_workspace_subscriptions_exclude_private_projects` |
| Reconnect | Disconnect, commit changes, reconnect with query token, rejoin, REST refetch, then receive new events | PASS | `Signalr_authorizes_subscriptions_delivers_compact_events_and_recovers_by_refetch_after_reconnect` |

### Server events

| Event | Verified behavior | Result | Evidence |
| --- | --- | --- | --- |
| `ProjectUpdated` | Work-item creation and comment create/edit/delete have correct type/entity ID; payload excludes title/body; overlap retains a stable event ID; revoked access stops delivery | PASS | `Signalr_comment_events_and_notification_ids_match_committed_rest_resources`; reconnect, unsubscribe, and revocation journeys |
| `NotificationReceived` | Assignment and mention notification IDs match notifications already retrievable through authorized REST; comment edits retaining the same mention and deletion emit no extra notification | PASS | `Signalr_comment_events_and_notification_ids_match_committed_rest_resources` |

### Email, overdue, and background processing

| Feature | Verified behavior | Result | Evidence |
| --- | --- | --- | --- |
| Existing invitation creation endpoint | Queues encrypted invitation URL; in-app invitation is visible to the invited account; fake SMTP receives the link; revocation hides/cancels delivery | PASS | `Invitation_email_is_encrypted_background_delivery_and_revocation_hides_notification` |
| Notification email | No SMTP means in-app only; deleted mentions/removed access cancel queued email; transient retries stop at the limit; slow send does not lock workspace writes or block real-time; in-flight body contains no private resource data | PASS | `PhaseFourIntegrationTests` and `PhaseFourReliabilityTests` |
| Overdue scanner | UTC due-date boundary; completed/archived/unassigned exclusions; concurrent scans deduplicate; due-date change allows a new reminder | PASS | `Overdue_checks_use_utc_skip_completed_archived_and_unassigned_and_deduplicate_concurrent_runs` |
| Worker/outbox | Independent job loops; scanner failure does not stop other queues; crash claims recover; bad events become terminal; retries avoid duplicate persisted notifications; rollback is not published | PASS | `PhaseFourReliabilityTests` |

Real Gmail inbox delivery, a deployed browser/reverse proxy, and a Redis backplane were not exercised. No real email was sent. The original endpoint-verification run added three integration tests without changing product behavior. The subsequent module-validation change adds two integration tests and explicitly rejects empty IDs as described above.

## Phase 4 P2 fixes and simplification

- SMTP no longer holds a workspace lock. Eligibility is checked immediately before sending; notification email is generic and contains no project/resource IDs or private text. Queued invitation links still require valid authorization at acceptance.
- Overdue, outbox, and email run in independent loops, each with its own scope, timeout, interval, and error handling. Scanner failures and slow SMTP no longer stop other jobs.
- Notification list validation uses `PaginationValidator` directly. The hub uses SignalR groups without a separate connection subscription registry; clients deduplicate stable event IDs.
- Built-in HTTP logging records method/path/status/duration and omits query strings, headers, and bodies. The existing raw hosting logs remain suppressed to protect SignalR query tokens.
- No schema or new migration changes were required for these fixes. Full build/test compilation passed without warnings; EF Core reports no pending model changes, and Compose configuration validation passes.

## Fixed regression cases

There are **no failed cases in the latest run**. These six cases previously returned 500 in Development (the environment configured by `backend/compose.yaml`). All now return `400 application/problem+json` with `status: 400`. Production still returns the expected 400.

| Operation | Input | Before: Development | After: Development | After: Production |
| --- | --- | ---: | ---: | ---: |
| PUT `/api/v1/workspaces/{workspaceId}` | Incomplete JSON: `{` | 500 | 400 | 400 |
| PUT `/api/v1/workspaces/{workspaceId}` | Wrong field type: `{"name":123}` | 500 | 400 | 400 |
| PUT `/api/v1/workspaces/{workspaceId}` | JSON body: `null` | 500 | 400 | 400 |
| GET `/api/v1/workspaces/{workspaceId}/projects` | `?page=abc` | 500 | 400 | 400 |
| GET `/api/v1/workspaces/{workspaceId}/projects` | `?archived=invalid` | 500 | 400 | 400 |
| GET `/api/v1/workspaces/{workspaceId}/projects` | `?leadUserId=not-a-uuid` | 500 | 400 | 400 |

The malformed workspace requests leave workspace data unchanged, as verified by subsequent GET requests in the regression tests.

### Cause and correction

In Development, malformed JSON/body or query values throw `Microsoft.AspNetCore.Http.BadHttpRequestException`. The generic exception handler previously returned 500 for these exceptions. [Program.cs](src/FlowForge.Api/Program.cs#L68) now configures `ExceptionHandlerOptions.StatusCodeSelector` to use `BadHttpRequestException.StatusCode`; unexpected exceptions retain the 500 fallback. This preserves the framework's client error status and the existing problem-details response format.

With a valid Owner/Admin token and an existing workspace, these requests now return 400 in both Development and Production:

```sh
curl -i -X PUT "$BASE/api/v1/workspaces/$WORKSPACE_ID" \
  -H "Authorization: Bearer $OWNER_TOKEN" \
  -H 'Content-Type: application/json' --data-binary '{'

curl -i "$BASE/api/v1/workspaces/$WORKSPACE_ID/projects?page=abc" \
  -H "Authorization: Bearer $OWNER_TOKEN"
```

## Historical Phase 1–4 passing coverage

- Identity: registration and duplicate normalization, login failures, password hashing, current-user responses, access expiry/issuer/audience validation, refresh rotation and replay, independent session families, logout revocation/idempotence, inactive accounts, and IP rate limits.
- Email verification: authentication, correct/incorrect/expired/single-use OTPs, five-attempt limits under concurrent guesses, malformed codes, leading zeros, resend cooldown/replacement, SMTP configuration and simulated delivery failures, persistent verified status, and the legacy OTP migration.
- Workspaces: creation, listing, read/rename, member listing/addition/role changes/removal, immediate permission updates, ownership transfer, concurrent transfer serialization, and the database owner invariant.
- Invitations: create/list/preview/accept/reject/revoke, role restrictions, wrong recipient, unverified email, expiry and terminal states, stale inviter authority, duplicate protection, and concurrent acceptance.
- Projects: create/read/update, visibility and role rules, cross-workspace isolation, search/filter/sort/pagination, unique keys, lead changes, member add/list/remove, retained membership history, optimistic version conflicts, archive/restore idempotence, and archived mutation rejection.
- Work items: all 26 Phase 3 route operations in an HTTP endpoint journey; concurrent item-number allocation, assignment, statuses, search/filter/sort, label/dependency/subtask changes, blocked state, archive/restore, stale versions, project permissions, and cross-project isolation.
- Collaboration: comment mentions and moderation, time entries, private attachment validation/upload/download/deletion, item-scoped audit before/after values, domain-event/outbox persistence, and attachment cleanup after a deferred database commit failure.
- Notifications: recipient-only paginated lists, read-one/read-all, assignment and mention rules, revoked access, optional SMTP, encrypted invitation bodies, UTC overdue reminders, concurrent scan/dispatch, bounded retries, crash recovery, rollback, and registered hosted services.
- SignalR: authenticated connections (header/query token), workspace/project subscription authorization, safe post-commit events, stable event IDs across overlapping subscriptions, membership revocation, and reconnect with REST refetch.
- Infrastructure: all seven migrations on a fresh database, Phase 3-to-4 upgrade and notification scope/uniqueness constraints, Phase 2-to-3 and earlier Phase 3 upgrades, activity backfill, readiness with available/unavailable PostgreSQL, liveness, Development/Production OpenAPI behavior, missing/short JWT keys, uniqueness, and composite foreign keys.

### Automated results by test class

| Test class | Passed | Failed |
| --- | ---: | ---: |
| `BackendCoverageTests` | 16 | 0 |
| `EmailVerificationMigrationTests` | 1 | 0 |
| `HostConfigurationTests` | 3 | 0 |
| `PasswordHashingTests` | 1 | 0 |
| `PhaseTwoIntegrationTests` | 17 | 0 |
| `PhaseTwoRulesTests` | 8 | 0 |
| `PhaseFourIntegrationTests` | 7 | 0 |
| `PhaseFourLoggingTests` | 1 | 0 |
| `PhaseFourMigrationTests` | 1 | 0 |
| `PhaseFourRealtimeTests` | 5 | 0 |
| `PhaseFourReliabilityTests` | 12 | 0 |
| `PhaseThreeCoverageTests` | 5 | 0 |
| `PhaseThreeIntegrationTests` | 2 | 0 |
| `PhaseThreeMigrationTests` | 2 | 0 |
| `RegisterRequestValidationTests` | 13 | 0 |
| `RequestValidationTests` | 35 | 0 |
| `ScaffoldSmokeTests` | 1 | 0 |
| `SystemAuditTests` | 7 | 0 |
| `WorkspacePermissionsTests` | 6 | 0 |

## Historical Phase 1–4 live endpoint results

PASS means the exercised responses matched expectations. The Phase 2 rows below are from the 2026-10-16 live run; the Phase 3 rows are from a new 2026-10-17 Development API process bound to `127.0.0.1:5187` with disposable PostgreSQL 17. The Phase 3 journey registered a user, created a workspace and project, then exercised every Phase 3 route operation, including a PDF upload and download. A 503 for missing SMTP and a 400 for OTP confirmation without a challenge are expected historical checks, not successful Gmail delivery. Successful OTP delivery/confirmation paths use `TestEmailSender` in the integration suite.

| Method | Route | HTTP checks passed / total | Result |
| --- | --- | ---: | --- |
| POST | `/api/v1/auth/email-verification/confirm` | 1 / 1 | PASS |
| POST | `/api/v1/auth/email-verification/request` | 1 / 1 | PASS |
| POST | `/api/v1/auth/login` | 2 / 2 | PASS |
| POST | `/api/v1/auth/logout` | 2 / 2 | PASS |
| GET | `/api/v1/auth/me` | 6 / 6 | PASS |
| POST | `/api/v1/auth/refresh` | 2 / 2 | PASS |
| POST | `/api/v1/auth/register` | 6 / 6 | PASS |
| POST | `/api/v1/invitations/accept` | 3 / 3 | PASS |
| POST | `/api/v1/invitations/reject` | 1 / 1 | PASS |
| GET | `/api/v1/invitations/{invitationId}` | 1 / 1 | PASS |
| GET | `/api/v1/workspaces` | 1 / 1 | PASS |
| POST | `/api/v1/workspaces` | 1 / 1 | PASS |
| GET | `/api/v1/workspaces/{workspaceId}` | 4 / 4 | PASS |
| PUT | `/api/v1/workspaces/{workspaceId}` | 6 / 6 | PASS |
| GET | `/api/v1/workspaces/{workspaceId}/invitations` | 1 / 1 | PASS |
| POST | `/api/v1/workspaces/{workspaceId}/invitations` | 3 / 3 | PASS |
| POST | `/api/v1/workspaces/{workspaceId}/invitations/{invitationId}/revoke` | 1 / 1 | PASS |
| GET | `/api/v1/workspaces/{workspaceId}/members` | 1 / 1 | PASS |
| POST | `/api/v1/workspaces/{workspaceId}/members` | 1 / 1 | PASS |
| DELETE | `/api/v1/workspaces/{workspaceId}/members/{userId}` | 1 / 1 | PASS |
| PUT | `/api/v1/workspaces/{workspaceId}/members/{userId}/role` | 1 / 1 | PASS |
| POST | `/api/v1/workspaces/{workspaceId}/ownership/transfer` | 1 / 1 | PASS |
| GET | `/api/v1/workspaces/{workspaceId}/projects` | 4 / 4 | PASS |
| POST | `/api/v1/workspaces/{workspaceId}/projects` | 1 / 1 | PASS |
| GET | `/api/v1/workspaces/{workspaceId}/projects/{projectId}` | 4 / 4 | PASS |
| PUT | `/api/v1/workspaces/{workspaceId}/projects/{projectId}` | 3 / 3 | PASS |
| POST | `/api/v1/workspaces/{workspaceId}/projects/{projectId}/archive` | 2 / 2 | PASS |
| GET | `/api/v1/workspaces/{workspaceId}/projects/{projectId}/members` | 1 / 1 | PASS |
| POST | `/api/v1/workspaces/{workspaceId}/projects/{projectId}/members` | 1 / 1 | PASS |
| DELETE | `/api/v1/workspaces/{workspaceId}/projects/{projectId}/members/{userId}` | 3 / 3 | PASS |
| POST | `/api/v1/workspaces/{workspaceId}/projects/{projectId}/restore` | 1 / 1 | PASS |
| GET | `/api/v1/workspaces/{workspaceId}/projects/{projectId}/statuses` | 1 / 1 | PASS |
| GET | `/api/v1/workspaces/{workspaceId}/projects/{projectId}/labels` | 1 / 1 | PASS |
| POST | `/api/v1/workspaces/{workspaceId}/projects/{projectId}/labels` | 1 / 1 | PASS |
| PUT | `/api/v1/workspaces/{workspaceId}/projects/{projectId}/labels/{labelId}` | 1 / 1 | PASS |
| DELETE | `/api/v1/workspaces/{workspaceId}/projects/{projectId}/labels/{labelId}` | 1 / 1 | PASS |
| GET | `/api/v1/workspaces/{workspaceId}/projects/{projectId}/work-items` | 1 / 1 | PASS |
| POST | `/api/v1/workspaces/{workspaceId}/projects/{projectId}/work-items` | 2 / 2 | PASS |
| GET | `/api/v1/workspaces/{workspaceId}/projects/{projectId}/work-items/{itemId}` | 1 / 1 | PASS |
| PUT | `/api/v1/workspaces/{workspaceId}/projects/{projectId}/work-items/{itemId}` | 1 / 1 | PASS |
| POST | `/api/v1/workspaces/{workspaceId}/projects/{projectId}/work-items/{itemId}/archive` | 1 / 1 | PASS |
| POST | `/api/v1/workspaces/{workspaceId}/projects/{projectId}/work-items/{itemId}/restore` | 1 / 1 | PASS |
| POST | `/api/v1/workspaces/{workspaceId}/projects/{projectId}/work-items/{itemId}/dependencies` | 1 / 1 | PASS |
| DELETE | `/api/v1/workspaces/{workspaceId}/projects/{projectId}/work-items/{itemId}/dependencies/{dependsOnId}` | 1 / 1 | PASS |
| GET | `/api/v1/workspaces/{workspaceId}/projects/{projectId}/work-items/{itemId}/comments` | 1 / 1 | PASS |
| POST | `/api/v1/workspaces/{workspaceId}/projects/{projectId}/work-items/{itemId}/comments` | 1 / 1 | PASS |
| PUT | `/api/v1/workspaces/{workspaceId}/projects/{projectId}/work-items/{itemId}/comments/{commentId}` | 1 / 1 | PASS |
| DELETE | `/api/v1/workspaces/{workspaceId}/projects/{projectId}/work-items/{itemId}/comments/{commentId}` | 1 / 1 | PASS |
| GET | `/api/v1/workspaces/{workspaceId}/projects/{projectId}/work-items/{itemId}/time-entries` | 1 / 1 | PASS |
| POST | `/api/v1/workspaces/{workspaceId}/projects/{projectId}/work-items/{itemId}/time-entries` | 1 / 1 | PASS |
| PUT | `/api/v1/workspaces/{workspaceId}/projects/{projectId}/work-items/{itemId}/time-entries/{entryId}` | 1 / 1 | PASS |
| DELETE | `/api/v1/workspaces/{workspaceId}/projects/{projectId}/work-items/{itemId}/time-entries/{entryId}` | 1 / 1 | PASS |
| GET | `/api/v1/workspaces/{workspaceId}/projects/{projectId}/work-items/{itemId}/attachments` | 1 / 1 | PASS |
| POST | `/api/v1/workspaces/{workspaceId}/projects/{projectId}/work-items/{itemId}/attachments` | 1 / 1 | PASS |
| GET | `/api/v1/workspaces/{workspaceId}/projects/{projectId}/work-items/{itemId}/attachments/{attachmentId}` | 1 / 1 | PASS |
| DELETE | `/api/v1/workspaces/{workspaceId}/projects/{projectId}/work-items/{itemId}/attachments/{attachmentId}` | 1 / 1 | PASS |
| GET | `/api/v1/workspaces/{workspaceId}/activity` | 1 / 1 | PASS |
| GET | `/health/live` | 1 / 1 | PASS |
| GET | `/health/ready` | 1 / 1 | PASS |
| GET | `/openapi/v1.json` | 1 / 1 | PASS |

## Historical Phase 1–4 automated test results

The original outcomes below came from the after-fix VSTest TRX output; the Phase 3 rows were checked against the latest `phase3.trx` and `backend-current.trx`. Parameterized test rows are listed separately.

### BackendCoverageTests

| Result | Test case |
| --- | --- |
| PASSED | `Expired_refresh_and_inactive_accounts_cannot_authenticate` |
| PASSED | `Healthy_database_is_reported_ready_and_invalid_routes_are_rejected` |
| PASSED | `Invalid_query_value_types_return_a_client_error(query: "archived=invalid")` |
| PASSED | `Invalid_query_value_types_return_a_client_error(query: "leadUserId=not-a-uuid")` |
| PASSED | `Invalid_query_value_types_return_a_client_error(query: "page=abc")` |
| PASSED | `Invitation_pages_terminal_states_and_admin_role_restrictions_are_enforced` |
| PASSED | `Login_refresh_replay_and_logout_enforce_session_family_boundaries` |
| PASSED | `Malformed_json_returns_a_client_error_without_mutating_the_workspace(body: "null")` |
| PASSED | `Malformed_json_returns_a_client_error_without_mutating_the_workspace(body: "{")` |
| PASSED | `Malformed_json_returns_a_client_error_without_mutating_the_workspace(body: "{\"name\":123}")` |
| PASSED | `Project_filters_sorts_and_pagination_return_only_matching_authorized_rows` |
| PASSED | `Project_lead_changes_member_removal_and_archive_idempotency_preserve_versions_and_history` |
| PASSED | `Protected_endpoint_groups_reject_missing_expired_and_invalid_tokens` |
| PASSED | `Rate_limits_apply_per_client_and_refresh_logout_share_a_budget` |
| PASSED | `Registration_normalizes_identity_and_persists_only_password_and_refresh_hashes` |
| PASSED | `Workspace_reads_renames_role_changes_and_removal_use_current_membership` |

### EmailVerificationMigrationTests

| Result | Test case |
| --- | --- |
| PASSED | `Otp_upgrade_expires_legacy_tokens_and_preserves_previously_verified_accounts` |

### HostConfigurationTests

| Result | Test case |
| --- | --- |
| PASSED | `Missing_or_short_signing_keys_prevent_startup(key: "")` |
| PASSED | `Missing_or_short_signing_keys_prevent_startup(key: "too-short")` |
| PASSED | `Production_hides_openapi_and_unavailable_database_fails_readiness_but_not_liveness` |

### PasswordHashingTests

| Result | Test case |
| --- | --- |
| PASSED | `Hash_is_salted_and_verifies_only_the_original_password` |

### PhaseTwoIntegrationTests

| Result | Test case |
| --- | --- |
| PASSED | `Concurrent_project_creation_and_membership_addition_preserve_unique_constraints` |
| PASSED | `Database_rejects_cross_workspace_project_membership_and_leads` |
| PASSED | `Expired_verification_and_delivery_failures_never_verify_the_user` |
| PASSED | `Feature_endpoints_return_field_validation_errors_after_authorizing_the_request` |
| PASSED | `Invitation_can_precede_registration_and_acceptance_is_atomic` |
| PASSED | `Invitation_roles_identity_expiration_revocation_and_rejection_are_enforced` |
| PASSED | `Invitations_do_not_retain_a_removed_inviters_authority` |
| PASSED | `Malformed_codes_count_as_attempts_and_leading_zero_codes_are_supported` |
| PASSED | `Otp_attempt_limit_is_persisted_under_concurrent_wrong_guesses` |
| PASSED | `Ownership_transfer_requires_a_verified_active_member_and_serializes_concurrent_transfers` |
| PASSED | `Project_validation_unique_keys_search_pagination_and_archive_behavior` |
| PASSED | `Projects_enforce_explicit_membership_role_rules_and_workspace_isolation` |
| PASSED | `Removing_members_preserves_history_and_removes_project_access_and_lead_assignments` |
| PASSED | `Resending_replaces_the_old_code_and_uses_a_ten_minute_expiry` |
| PASSED | `Verification_requires_authentication_and_matching_unexpired_single_use_code` |
| PASSED | `Verified_accounts_do_not_receive_another_code_and_remain_verified_after_sign_in` |
| PASSED | `Workspace_creation_and_member_roles_preserve_the_owner_and_database_rejects_ownerless_creation` |

### PhaseTwoRulesTests

| Result | Test case |
| --- | --- |
| PASSED | `Invitation_state_preserves_terminal_outcomes_and_expired_history` |
| PASSED | `Otp_codes_are_six_digits_and_hashes_are_bound_to_the_user_challenge_and_secret` |
| PASSED | `Project_permissions_distinguish_metadata_management_and_resource_writes(role: Admin, create: True, manage: True, write: True)` |
| PASSED | `Project_permissions_distinguish_metadata_management_and_resource_writes(role: Developer, create: False, manage: False, write: True)` |
| PASSED | `Project_permissions_distinguish_metadata_management_and_resource_writes(role: Manager, create: False, manage: True, write: True)` |
| PASSED | `Project_permissions_distinguish_metadata_management_and_resource_writes(role: Owner, create: True, manage: True, write: True)` |
| PASSED | `Project_permissions_distinguish_metadata_management_and_resource_writes(role: Viewer, create: False, manage: False, write: False)` |
| PASSED | `Tokens_have_random_entropy_and_stable_nonplaintext_hashes` |

### PhaseFourIntegrationTests

| Result | Test |
| --- | --- |
| PASSED | `Assignment_mentions_read_apis_and_email_are_idempotent_and_authorized` |
| PASSED | `Comment_edits_notify_only_new_mentions_and_deletions_cancel_pending_email` |
| PASSED | `Empty_notification_id_returns_field_validation_problem_before_resource_lookup` |
| PASSED | `Invitation_email_is_encrypted_background_delivery_and_revocation_hides_notification` |
| PASSED | `Notification_endpoints_enforce_authentication_paging_and_read_all_recipient_isolation` |
| PASSED | `Overdue_checks_use_utc_skip_completed_archived_and_unassigned_and_deduplicate_concurrent_runs` |
| PASSED | `Removed_members_cannot_read_or_receive_queued_project_notifications` |

### PhaseFourLoggingTests

| Result | Test |
| --- | --- |
| PASSED | `Request_logs_preserve_method_path_status_and_duration_without_query_tokens` |

### PhaseFourMigrationTests

| Result | Test |
| --- | --- |
| PASSED | `Phase_three_upgrade_preserves_pending_events_and_enforces_notification_scope_and_deduplication` |

### PhaseFourRealtimeTests

| Result | Test |
| --- | --- |
| PASSED | `Existing_signalr_connection_stops_receiving_project_events_after_access_revocation` |
| PASSED | `Hub_validates_empty_ids_for_all_subscription_methods_and_binding_rejects_malformed_guids` |
| PASSED | `Leaving_project_and_workspace_stops_delivery_and_workspace_subscriptions_exclude_private_projects` |
| PASSED | `Signalr_authorizes_subscriptions_delivers_compact_events_and_recovers_by_refetch_after_reconnect` |
| PASSED | `Signalr_comment_events_and_notification_ids_match_committed_rest_resources` |

### PhaseFourReliabilityTests

| Result | Test |
| --- | --- |
| PASSED | `Concurrent_dispatchers_and_recovered_claims_process_each_notification_once` |
| PASSED | `Disabled_smtp_keeps_in_app_notifications_available_without_creating_email_jobs` |
| PASSED | `Email_transient_failure_backs_off_and_stops_at_attempt_limit` |
| PASSED | `Failing_overdue_scan_does_not_block_outbox_or_email_jobs` |
| PASSED | `Hosted_worker_runs_registered_overdue_dispatch_and_email_services` |
| PASSED | `Malformed_events_and_exhausted_crash_claims_fail_without_poisoning_other_messages` |
| PASSED | `Realtime_failure_retries_after_commit_without_duplicate_notifications_or_email` |
| PASSED | `Retry_classification_does_not_retry_permanent_smtp_failures(status: GeneralFailure, retry: True)` |
| PASSED | `Retry_classification_does_not_retry_permanent_smtp_failures(status: MailboxBusy, retry: True)` |
| PASSED | `Retry_classification_does_not_retry_permanent_smtp_failures(status: MailboxUnavailable, retry: False)` |
| PASSED | `Rolled_back_events_are_never_published_or_notified` |
| PASSED | `Slow_email_does_not_block_workspace_writes_or_realtime_dispatch_and_contains_no_private_data` |

### PhaseThreeCoverageTests

| Result | Test case |
| --- | --- |
| PASSED | `Comment_time_and_attachment_validation_and_moderation` |
| PASSED | `Labels_relationships_and_activity_respect_project_scope` |
| PASSED | `Remaining_phase_three_endpoint_operations_complete_a_project_journey` |
| PASSED | `Upload_removes_private_file_if_the_database_commit_fails` |
| PASSED | `Work_item_filters_activity_details_and_outbox_follow_committed_changes` |

### PhaseThreeIntegrationTests

| Result | Test case |
| --- | --- |
| PASSED | `Collaboration_activity_and_file_access_are_project_scoped` |
| PASSED | `Work_items_assignments_cycles_archiving_and_permissions` |

### PhaseThreeMigrationTests

| Result | Test case |
| --- | --- |
| PASSED | `Completion_upgrade_backfills_existing_work_item_activity` |
| PASSED | `Initial_phase_three_upgrade_seeds_statuses_for_existing_projects` |

### RegisterRequestValidationTests

| Result | Test case |
| --- | --- |
| PASSED | `Accepts_strong_passwords_at_length_boundaries(length: 128)` |
| PASSED | `Accepts_strong_passwords_at_length_boundaries(length: 8)` |
| PASSED | `Registration_returns_password_validation_errors_before_accessing_the_database` |
| PASSED | `Rejects_passwords_over_the_maximum_length` |
| PASSED | `Rejects_short_or_weak_passwords(password: "")` |
| PASSED | `Rejects_short_or_weak_passwords(password: "ABCDEF1!")` |
| PASSED | `Rejects_short_or_weak_passwords(password: "Aa1!abc")` |
| PASSED | `Rejects_short_or_weak_passwords(password: "Abcdef1 ")` |
| PASSED | `Rejects_short_or_weak_passwords(password: "Abcdef12")` |
| PASSED | `Rejects_short_or_weak_passwords(password: "Abcdefg!")` |
| PASSED | `Rejects_short_or_weak_passwords(password: "abcdef1!")` |
| PASSED | `Rejects_short_or_weak_passwords(password: "abcdefgh")` |
| PASSED | `Rejects_short_or_weak_passwords(password: null)` |

### RequestValidationTests

| Result | Test case |
| --- | --- |
| PASSED | `Empty_member_and_owner_identifiers_are_reported_on_the_correct_fields` |
| PASSED | `Identity_endpoints_return_field_errors_before_accessing_the_database` |
| PASSED | `Identity_validation_does_not_apply_registration_complexity_to_login` |
| PASSED | `Invalid_project_keys_are_rejected(key: "1AB")` |
| PASSED | `Invalid_project_keys_are_rejected(key: "A")` |
| PASSED | `Invalid_project_keys_are_rejected(key: "ABCDEFGHIJK")` |
| PASSED | `Invalid_project_keys_are_rejected(key: "FLOW-KEY")` |
| PASSED | `Invitations_and_membership_requests_report_email_and_role_errors_together` |
| PASSED | `Numeric_and_unknown_roles_are_rejected(role: " Admin ")` |
| PASSED | `Numeric_and_unknown_roles_are_rejected(role: "1")` |
| PASSED | `Numeric_and_unknown_roles_are_rejected(role: "Unknown")` |
| PASSED | `Numeric_and_unknown_roles_are_rejected(role: null)` |
| PASSED | `Otp_accepts_leading_zeros` |
| PASSED | `Otp_requires_six_ascii_digits(code: "12345")` |
| PASSED | `Otp_requires_six_ascii_digits(code: "1234567")` |
| PASSED | `Otp_requires_six_ascii_digits(code: "abcdef")` |
| PASSED | `Otp_requires_six_ascii_digits(code: "１２３４５６")` |
| PASSED | `Otp_requires_six_ascii_digits(code: null)` |
| PASSED | `Pagination_accepts_defaults_and_boundaries(page: 1, pageSize: 1)` |
| PASSED | `Pagination_accepts_defaults_and_boundaries(page: 1000000, pageSize: 100)` |
| PASSED | `Pagination_accepts_defaults_and_boundaries(page: null, pageSize: null)` |
| PASSED | `Project_validation_collects_all_invalid_fields` |
| PASSED | `Query_validation_reports_pagination_search_sort_and_identifier_errors` |
| PASSED | `Role_syntax_accepts_named_roles_independently_of_assignment_permissions(role: "Admin")` |
| PASSED | `Role_syntax_accepts_named_roles_independently_of_assignment_permissions(role: "Developer")` |
| PASSED | `Role_syntax_accepts_named_roles_independently_of_assignment_permissions(role: "Manager")` |
| PASSED | `Role_syntax_accepts_named_roles_independently_of_assignment_permissions(role: "Viewer")` |
| PASSED | `Role_syntax_accepts_named_roles_independently_of_assignment_permissions(role: "owner")` |
| PASSED | `Valid_project_key_boundaries_and_same_day_dates_are_accepted(key: "AB")` |
| PASSED | `Valid_project_key_boundaries_and_same_day_dates_are_accepted(key: "ABCDEFGHIJ")` |
| PASSED | `Valid_project_key_boundaries_and_same_day_dates_are_accepted(key: "a1")` |
| PASSED | `Workspace_names_are_required_for_creation_and_update(name: "   ")` |
| PASSED | `Workspace_names_are_required_for_creation_and_update(name: "")` |
| PASSED | `Workspace_names_are_required_for_creation_and_update(name: null)` |
| PASSED | `Workspace_names_use_trimmed_length_boundaries` |

### ScaffoldSmokeTests

| Result | Test case |
| --- | --- |
| PASSED | `Host_exposes_liveness_and_openapi_in_development` |

### SystemAuditTests

| Result | Test |
| --- | --- |
| PASSED | `Concurrent_refresh_reuse_revokes_the_winning_replacement` |
| PASSED | `Logout_revokes_a_replacement_token_when_refresh_commits_concurrently` |
| PASSED | `Signed_tokens_with_missing_malformed_or_empty_subject_are_rejected_at_authentication(subject: "00000000-0000-0000-0000-000000000000")` |
| PASSED | `Signed_tokens_with_missing_malformed_or_empty_subject_are_rejected_at_authentication(subject: "not-a-guid")` |
| PASSED | `Signed_tokens_with_missing_malformed_or_empty_subject_are_rejected_at_authentication(subject: null)` |
| PASSED | `Time_entries_accept_offset_timestamps_and_persist_the_same_instant_in_utc(offsetHours: -5)` |
| PASSED | `Time_entries_accept_offset_timestamps_and_persist_the_same_instant_in_utc(offsetHours: 7)` |

### WorkspacePermissionsTests

| Result | Test case |
| --- | --- |
| PASSED | `Admin_cannot_assign_or_modify_owner_or_admin_roles` |
| PASSED | `Roles_grant_only_their_intended_permissions(role: Admin, manageMembers: True, manageProjects: True, writeWorkItems: True, transferOwnership: False)` |
| PASSED | `Roles_grant_only_their_intended_permissions(role: Developer, manageMembers: False, manageProjects: False, writeWorkItems: True, transferOwnership: False)` |
| PASSED | `Roles_grant_only_their_intended_permissions(role: Manager, manageMembers: False, manageProjects: True, writeWorkItems: True, transferOwnership: False)` |
| PASSED | `Roles_grant_only_their_intended_permissions(role: Owner, manageMembers: True, manageProjects: True, writeWorkItems: True, transferOwnership: True)` |
| PASSED | `Roles_grant_only_their_intended_permissions(role: Viewer, manageMembers: False, manageProjects: False, writeWorkItems: False, transferOwnership: False)` |

## Historical Phase 1–4 live HTTP checks

The Phase 2 rows are from the 2026-10-16 external-process run. The Phase 3 rows are from the 2026-10-17 external-process run against disposable PostgreSQL.

| Result | Environment | Check | Expected | Actual |
| --- | --- | --- | --- | --- |
| PASS | Development | Host liveness | 200 | 200 |
| PASS | Development | Database readiness | 200 | 200 |
| PASS | Development | Development OpenAPI | 200 | 200 |
| PASS | Development | Register owner | 201 | 201 |
| PASS | Development | Current user owner | 200 | 200 |
| PASS | Development | New account is unverified: owner | True | True |
| PASS | Development | Register member | 201 | 201 |
| PASS | Development | Current user member | 200 | 200 |
| PASS | Development | New account is unverified: member | True | True |
| PASS | Development | Register recipient | 201 | 201 |
| PASS | Development | Current user recipient | 200 | 200 |
| PASS | Development | New account is unverified: recipient | True | True |
| PASS | Development | Register rejectee | 201 | 201 |
| PASS | Development | Current user rejectee | 200 | 200 |
| PASS | Development | New account is unverified: rejectee | True | True |
| PASS | Development | Register outsider | 201 | 201 |
| PASS | Development | Current user outsider | 200 | 200 |
| PASS | Development | New account is unverified: outsider | True | True |
| PASS | Development | Register IP rate limit | 429 | 429 |
| PASS | Development | Wrong login password | 401 | 401 |
| PASS | Development | Login | 200 | 200 |
| PASS | Development | Refresh rotation | 200 | 200 |
| PASS | Development | Logout | 204 | 204 |
| PASS | Development | Revoked refresh rejected | 401 | 401 |
| PASS | Development | Empty logout idempotent | 204 | 204 |
| PASS | Development | Anonymous me | 401 | 401 |
| PASS | Development | Missing SMTP configuration | 503 | 503 |
| PASS | Development | OTP without a challenge | 400 | 400 |
| PASS | Development | Create workspace | 201 | 201 |
| PASS | Development | List workspaces | 200 | 200 |
| PASS | Development | Get workspace | 200 | 200 |
| PASS | Development | Rename workspace | 200 | 200 |
| PASS | Development | List workspace members | 200 | 200 |
| PASS | Development | Add workspace member | 201 | 201 |
| PASS | Development | Change workspace role | 200 | 200 |
| PASS | Development | Outsider workspace isolation | 404 | 404 |
| PASS | Development | Manager cannot rename workspace | 403 | 403 |
| PASS | Development | Empty workspace name | 400 | 400 |
| PASS | Development | Create project | 201 | 201 |
| PASS | Development | Project key uppercased | True | True |
| PASS | Development | List projects | 200 | 200 |
| PASS | Development | Read project | 200 | 200 |
| PASS | Development | Unassigned manager cannot read | 404 | 404 |
| PASS | Development | Add project member | 201 | 201 |
| PASS | Development | Read explicit project members | 200 | 200 |
| PASS | Development | Read current project version | 200 | 200 |
| PASS | Development | Manager updates project and sets lead | 200 | 200 |
| PASS | Development | Stale project update | 409 | 409 |
| PASS | Development | Cannot remove project lead | 409 | 409 |
| PASS | Development | Archive project | 200 | 200 |
| PASS | Development | Archive is idempotent | 200 | 200 |
| PASS | Development | Idempotent archive keeps version | True | True |
| PASS | Development | Archived member mutation rejected | 409 | 409 |
| PASS | Development | Restore project | 200 | 200 |
| PASS | Development | Clear lead | 200 | 200 |
| PASS | Development | Remove project member | 204 | 204 |
| PASS | Development | Removed project member loses access | 404 | 404 |
| PASS | Development | Create invitation | 201 | 201 |
| PASS | Development | List invitations | 200 | 200 |
| PASS | Development | Invitation list contains no raw token | True | True |
| PASS | Development | Recipient invitation preview | 200 | 200 |
| PASS | Development | Wrong recipient cannot consume | 404 | 404 |
| PASS | Development | Accept invitation | 204 | 204 |
| PASS | Development | Invitation replay rejected | 409 | 409 |
| PASS | Development | Create second invitation | 201 | 201 |
| PASS | Development | Reject invitation | 204 | 204 |
| PASS | Development | Create invitation to revoke | 201 | 201 |
| PASS | Development | Revoke invitation | 204 | 204 |
| PASS | Development | Remove workspace member | 204 | 204 |
| PASS | Development | Removed workspace member loses access | 404 | 404 |
| PASS | Development | Transfer ownership | 204 | 204 |
| PASS | Development | Former owner becomes Admin | 200 | 200 |
| PASS | Development | Transfer persisted former owner role | True | True |
| PASS | Development | Malformed JSON: { | 400 | 400 |
| PASS | Development | Malformed JSON: {"name":123} | 400 | 400 |
| PASS | Development | Malformed JSON: null | 400 | 400 |
| PASS | Development | Invalid query type: page=abc | 400 | 400 |
| PASS | Development | Invalid query type: archived=invalid | 400 | 400 |
| PASS | Development | Invalid query type: leadUserId=not-a-uuid | 400 | 400 |
| PASS | Production | Malformed JSON: { | 400 | 400 |
| PASS | Production | Malformed JSON: {"name":123} | 400 | 400 |
| PASS | Production | Malformed JSON: null | 400 | 400 |
| PASS | Production | Invalid query type: page=abc | 400 | 400 |
| PASS | Production | Invalid query type: archived=invalid | 400 | 400 |
| PASS | Production | Invalid query type: leadUserId=not-a-uuid | 400 | 400 |
| PASS | Production | Production OpenAPI hidden | 404 | 404 |
| PASS | Production | Production readiness | 200 | 200 |
| PASS | Development, Phase 3 | List project statuses | 200 | 200 |
| PASS | Development, Phase 3 | Create label | 201 | 201 |
| PASS | Development, Phase 3 | List labels | 200 | 200 |
| PASS | Development, Phase 3 | Update label | 200 | 200 |
| PASS | Development, Phase 3 | Create prerequisite work item | 201 | 201 |
| PASS | Development, Phase 3 | Create main work item | 201 | 201 |
| PASS | Development, Phase 3 | List work items | 200 | 200 |
| PASS | Development, Phase 3 | Read work item | 200 | 200 |
| PASS | Development, Phase 3 | Add dependency | 201 | 201 |
| PASS | Development, Phase 3 | Remove dependency | 204 | 204 |
| PASS | Development, Phase 3 | Update work item | 200 | 200 |
| PASS | Development, Phase 3 | Create comment | 201 | 201 |
| PASS | Development, Phase 3 | List comments | 200 | 200 |
| PASS | Development, Phase 3 | Update comment | 200 | 200 |
| PASS | Development, Phase 3 | Create time entry | 201 | 201 |
| PASS | Development, Phase 3 | List time entries | 200 | 200 |
| PASS | Development, Phase 3 | Update time entry | 200 | 200 |
| PASS | Development, Phase 3 | Upload PDF attachment | 201 | 201 |
| PASS | Development, Phase 3 | List attachments | 200 | 200 |
| PASS | Development, Phase 3 | Download PDF attachment | 200 | 200 |
| PASS | Development, Phase 3 | Delete attachment | 204 | 204 |
| PASS | Development, Phase 3 | Read item-scoped activity | 200 | 200 |
| PASS | Development, Phase 3 | Delete time entry | 204 | 204 |
| PASS | Development, Phase 3 | Delete comment | 204 | 204 |
| PASS | Development, Phase 3 | Archive work item | 200 | 200 |
| PASS | Development, Phase 3 | Restore work item | 200 | 200 |
| PASS | Development, Phase 3 | Delete label | 204 | 204 |

## Reproduce the historical automated runs

Phase 4 uses the same isolated PostgreSQL fixture and CI configuration. With `FLOWFORGE_TEST_CONNECTION` set to a test server, run:

```sh
dotnet test FlowForge.sln --no-restore -m:1 /p:UseSharedCompilation=false --filter 'FullyQualifiedName~PhaseFour'
dotnet test FlowForge.sln --no-restore -m:1 /p:UseSharedCompilation=false
```

The latest TRX artifact was `/tmp/flowforge-system-audit-results/system-final.trx`; its 143 outcomes were checked against the totals above. The following commands reproduce the historical Phase 3 run.

From `backend/`, start an isolated PostgreSQL 17 server. Set `FLOWFORGE_TEST_CONNECTION` to its admin connection string; the fixture requires permission to create/drop uniquely named test databases. Then run:

```sh
dotnet restore FlowForge.sln -m:1
dotnet build FlowForge.sln --no-restore -m:1 /p:UseSharedCompilation=false
dotnet test FlowForge.sln --no-build -m:1 /p:UseSharedCompilation=false \
  --filter 'FullyQualifiedName~PhaseThree' \
  --logger 'trx;LogFileName=phase3.trx' --results-directory /tmp/flowforge-phase3-results
dotnet test FlowForge.sln --no-build -m:1 /p:UseSharedCompilation=false \
  --logger 'trx;LogFileName=backend-current.trx' --results-directory /tmp/flowforge-phase3-results
```

The latest focused run exited with code 0: 9 passed, 0 failed, 0 skipped. The full run exited with code 0: 110 passed, 0 failed, 0 skipped. `dotnet ef migrations has-pending-model-changes --project src/FlowForge.Infrastructure --startup-project src/FlowForge.Api --no-build` reported no pending changes when run with the test connection and a valid `Jwt__SigningKey`. Without `FLOWFORGE_TEST_CONNECTION`, PostgreSQL-dependent tests are skipped, so that is not equivalent to these runs.

## Historical verification limits

- Phase 4 SignalR tests use real WebSocket/SignalR framing through ASP.NET Core TestServer, not a deployed browser or Redis backplane. The implementation targets a single API instance.
- SMTP retries deduplicate queued jobs; an ambiguous provider acceptance followed by process/commit failure can still result in a repeated inbox message. No real email was sent during tests.

- Real Gmail SMTP delivery and inbox receipt were not tested. The automated suite captures email in memory and simulates configuration/delivery failures. The live API was deliberately started without SMTP credentials.
- For live invitation accept/reject/ownership checks, only the disposable database's recipient accounts were marked verified through SQL. That setup does not count as a successful live OTP flow; the OTP success flow is covered by integration tests.
- These checks do not certify load capacity, penetration resistance, every possible input, or behavior on a deployed external host. No performance benchmark was run.
- The fix changes only exception-status selection. The six regression cases remain in the suite and now assert the 400 problem-details response.
- The latest live Phase 3 run used Development mode and a local disposable database. It exercised each route's successful journey; failure paths, authorization, concurrency, rollback, and migration upgrades are covered by the PostgreSQL integration suite rather than every combination being repeated in the external-process run.
