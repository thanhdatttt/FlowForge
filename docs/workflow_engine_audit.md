# Phase 5 backend completion audit

Reviewed on 2026-10-18 (+07) against the feature specification, database design, existing backend conventions, and the agreed backend-only scope. Workflow management and execution use the previously agreed creator-authority, version-pinning, current-state condition, disable/cancellation and non-replay policies.

## Task-by-task result

| Task | Result | Implementation and verification |
| --- | --- | --- |
| Define workflow behavior and permissions | Complete | `WorkflowAccess`, existing project resource permissions, documented creator authority, immutable scope, explicit disable and enablement-window rules. Integration tests cover Owners/Admins, assigned Managers, Developers/Viewers, outsiders, creator revocation and archived resources. |
| Entities, persistence and migration | Complete | Workflow, immutable version, execution and action-result tables; scope foreign keys, state/uniqueness constraints, immutable-version trigger and history-preserving downgrade guard. Fresh database and Phase 4 upgrade tests pass; EF reports no pending model changes. |
| Configuration validation and management APIs | Complete | Create/list/read/update/validate/enable/disable; shared typed definition rules, scoped reference checks, dedicated write/state validators, optimistic versions and pagination. Comment mentions now validate before creation/enablement and again at execution. |
| Reliable event scheduling | Complete | All seven triggers; workflow scheduling commits executions and action ledgers with an independent event checkpoint. Notification dispatch cannot consume workflow events. Concurrency, rollback, deduplication and historical-event non-replay tests pass. |
| Conditions | Complete | Typed equals/not-equals, list membership, empty/not-empty, priority/date comparisons, and actor/assignee/label/status/type matching under ALL/AND. Conditions evaluate once against current item state. Gregorian ISO dates now work under non-Gregorian host cultures. |
| Ordered actions | Complete | Assign/unassign, change status/priority, add/remove label, add comment, notify and queue email. Existing member/status/label/mention rules are shared with HTTP writes. All nine actions have an end-to-end integration test including audit, events and optimistic versions. |
| Durable execution and recovery | Complete | Five execution states, expiring claims, ownership checks, per-action transactions, bounded attempts/backoff, permanent-error handling and atomic completion ledgers. Actual cancellation and expired/exhausted-claim regressions verify recovery without duplicate committed effects. |
| Loop protection and calendar triggers | Complete | Server-owned execution ancestry plus bounded depth; independent UTC date scanning includes unassigned work and deduplicates item/date/type. Tests cover multi-rule loops, depth ceilings, trace-ID reuse and concurrent date scans. |
| Outcomes, audit and live delivery | Complete | Authorized history/details now expose the pinned version number and definition, source actor/correlation/origin/depth, ordered results and email delivery status. Activity links executions; WebSocket tests verify committed notification IDs and execution outcome events against REST. |
| Tests, documentation and repository style | Complete for backend/API | Each of the six API DTOs has its own file; shared configuration/context records are also separated from their rules. Startup validation, HTTP/integration, unit, migration and real SignalR framing tests pass. See `backend/TEST_RESULTS.md` for commands and artifacts. |

## Audit corrections

Four targeted assertions reproduced three defects before the fixes:

- Recovering an exhausted claim increased the attempt counter from 5 to 6. Recovery now preserves the configured limit, terminates unfinished work, and can acknowledge a committed final action without repeating it.
- Comment action mentions were checked only during execution. Validation/create/enable now reject unavailable mentioned users in the rule's scope, using the same mention parser as ordinary comments.
- ISO due-date validation/comparison depended on host culture. Parsing, comparison formatting and timer deduplication keys now explicitly use invariant Gregorian dates; Thai and Saudi Arabic culture regressions pass.

Execution history previously exposed only the pinned version UUID, leaving its old definition unavailable after editing a workflow. Both history and details now return that immutable version's number and definition, with source metadata. The version-pinning integration test verifies both old and new definitions through HTTP after an edit.

Additional checks confirm field-specific state-version errors, cancellation during an actual database action followed by lease recovery, and recovery after the last action committed on the final allowed attempt.

## Verification

- Full backend: **210 passed, 0 failed, 0 skipped** against disposable PostgreSQL 17.
- Phase 5: **67 passed**: 31 rule cases, 5 host-option cases, 29 integration/reliability cases, 1 SignalR WebSocket case and 1 migration case.
- Latest live Release verification: **66 checks passed** (47 HTTP responses and 19 state assertions); all **9/9 workflow OpenAPI operations** succeeded. Nine polling requests are recorded separately.
- Build: zero warnings/errors. Release publish succeeds.
- EF Core: no pending model changes; no additional migration is needed for this audit.
- `git diff --check` passes; each API DTO file contains one DTO matching its filename.

Earlier regression artifacts: `/tmp/flowforge-phase5-audit-results/phase5-audit-before.trx` (four reproduced failures), `phase5-audit-focused.trx` (67 passing Phase 5 cases), and `backend-phase5-audit-final.trx` (210 passing backend cases). Release output: `/tmp/flowforge-phase5-audit-publish`.

The latest endpoint matrix, all 67 automated case outcomes, individual live HTTP/state checks and reproduction commands are in [backend/TEST_RESULTS.md](../backend/TEST_RESULTS.md). Durable evidence is stored in [automated-results.json](../backend/test-results/phase5/automated-results.json) and [live-http.json](../backend/test-results/phase5/live-http.json). The latest raw TRX is `/tmp/flowforge-phase5-dotnet-results/backend-phase5-dotnet.trx`; Release output is `/tmp/flowforge-phase5-dotnet-publish`.

The live runner now uses the .NET console project `backend/tests/FlowForge.Api.SmokeTests`; the prior script has been removed. All 66 check records were compared against the earlier baseline and match exactly. Unexpected-response handling was verified separately: exit code 1 and a failed-check JSON report. See [runner-parity.json](../backend/test-results/phase5/runner-parity.json).

The agreed frontend/workflow builder remains deferred. OR conditions and a drag-and-drop builder remain optional. Real Gmail inbox delivery, deployment/load validation and a distributed SignalR backplane remain outside this backend phase. No required backend/API task remains open in this audit.
