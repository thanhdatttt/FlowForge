# Backend test results

Latest full backend audit: 2026-10-17 (+07), .NET SDK 10.0.112 and disposable PostgreSQL 17. All **143 automated tests passed**, including **26 Phase 4 cases** and **7 system-audit regressions**, with zero failures/skips. A separately running Release API passed **50 live checks** (46 HTTP requests and four state assertions). No application database or real SMTP service was used. See the full backend audit section for the three reproduced and corrected defects.

Phase 4 covers notification APIs and permissions, assignment/self-notification rules, new/edited mentions, encrypted invitation email, cancellation after revocation/removal, overdue UTC boundaries and deduplication, concurrent dispatchers/scanners, expiring claims, retry limits, safe errors, rollback isolation, hosted-worker processing, Phase 3 migration upgrades, and real SignalR WebSocket handshake/subscription/event/reconnect journeys through TestServer. New regressions verify that slow SMTP does not block workspace writes or live dispatch, revoked access during an in-flight email cannot expose private resource data (including earlier queued bodies), a failed overdue scanner does not block outbox/email, and HTTP logs preserve method/path/status/duration without query tokens. Email uses the in-memory test sender; real Gmail inbox delivery remains unverified.

The earlier Phase 3 retest and live HTTP evidence below are retained as historical results. The exception-handling regression run was performed on 2026-10-16 20:13 +07.

## Results

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

## Full backend audit

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

## Passing coverage

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

## Live endpoint results

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

## Every automated test result

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

## Every live HTTP check

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

## Reproduce the automated run

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

## Limits

- Phase 4 SignalR tests use real WebSocket/SignalR framing through ASP.NET Core TestServer, not a deployed browser or Redis backplane. The implementation targets a single API instance.
- SMTP retries deduplicate queued jobs; an ambiguous provider acceptance followed by process/commit failure can still result in a repeated inbox message. No real email was sent during tests.

- Real Gmail SMTP delivery and inbox receipt were not tested. The automated suite captures email in memory and simulates configuration/delivery failures. The live API was deliberately started without SMTP credentials.
- For live invitation accept/reject/ownership checks, only the disposable database's recipient accounts were marked verified through SQL. That setup does not count as a successful live OTP flow; the OTP success flow is covered by integration tests.
- These checks do not certify load capacity, penetration resistance, every possible input, or behavior on a deployed external host. No performance benchmark was run.
- The fix changes only exception-status selection. The six regression cases remain in the suite and now assert the 400 problem-details response.
- The latest live Phase 3 run used Development mode and a local disposable database. It exercised each route's successful journey; failure paths, authorization, concurrency, rollback, and migration upgrades are covered by the PostgreSQL integration suite rather than every combination being repeated in the external-process run.
