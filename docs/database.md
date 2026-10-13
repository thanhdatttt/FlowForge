# FlowForge database schema

Proposed PostgreSQL schema based on [feature_specification.md](feature_specification.md) and [proposal.md](proposal.md). This is a design for future EF Core migrations, not an applied migration. The feature specification is authoritative if the two docs differ.

## Conventions

- Table and column names use `snake_case`. `id` is a UUID primary key; other `*_id` columns are UUID foreign keys unless stated otherwise. `?` means nullable. Timestamps are UTC `timestamptz`; calendar dates are `date`. Mutable rows have `created_at` and `updated_at`.
- Foreign keys default to restricted deletion. Archive, deactivate, or soft-delete business records so comments and audit history survive. Use composite foreign keys including `workspace_id` or `project_id` where needed to block cross-scope references.
- Validate enum-like strings in the API and database. Store workflow configuration as validated JSONB, never executable user code. Store attachment bytes outside PostgreSQL.

## Identity and workspaces

| Table | Main columns | Constraints and purpose |
| --- | --- | --- |
| `users` | `id`, `email`, `normalized_email`, `password_hash`, `display_name`, `is_active`, timestamps | Unique normalized email; password hash only. |
| `refresh_sessions` | `id`, `user_id`, `token_hash`, `family_id`, `expires_at`, `revoked_at?`, `replaced_by_id?`, timestamps | Unique token hash. Rotate and revoke tokens atomically; detect reuse by token family. |
| `password_reset_tokens`* | `id`, `user_id`, `token_hash`, `expires_at`, `used_at?`, `created_at` | Unique token hash; expiring, single-use. |
| `workspaces` | `id`, `name`, `created_by_user_id`, timestamps | Workspace root. |
| `workspace_memberships` | `id`, `workspace_id`, `user_id`, `role`, `joined_at`, `removed_at?` | Roles: Owner, Admin, Manager, Developer, Viewer. Unique active `(workspace_id, user_id)` and unique active Owner per workspace. Retain removed rows. |
| `workspace_invitations` | `id`, `workspace_id`, `normalized_email`, `role`, `token_hash`, `invited_by_user_id`, `expires_at`, `accepted_at?`, `rejected_at?`, `revoked_at?` | Unique token hash; one usable invitation per workspace/email. Acceptance checks expiry and email in one transaction. |

A workspace must have **exactly one active Owner**. The unique index enforces at most one; creation, transfer, and removal must also enforce existence, ideally with a deferred database constraint trigger. `password_reset_tokens` exists only if email delivery is configured. The role-to-operation policy is enforced server-side, not inferred from these tables alone.

## Projects and work items

| Table | Main columns | Constraints and purpose |
| --- | --- | --- |
| `projects` | `id`, `workspace_id`, `project_key`, `name`, `description?`, `lead_membership_id?`, `starts_on?`, `target_on?`, `archived_at?`, `next_item_number bigint`, timestamps, `version bigint` | Unique `(workspace_id, project_key)`. Lead must be an active member of the same workspace. Archived projects are read-only by default. |
| `project_memberships` | `project_id`, `workspace_membership_id`, `joined_at`, `removed_at?` | Unique active pair; supports project-member lists and assigned-project permissions. Members must belong to the project's workspace. |
| `project_statuses` | `id`, `project_id`, `name`, `category`, `sort_order`, `is_active` | Unique `(project_id, name)`. Seed Todo, InProgress, InReview, Done, Blocked. `category` identifies completed work for reporting. |
| `work_items` | `id`, `workspace_id`, `project_id`, `item_number bigint`, `title`, `description?`, `type`, `status_id`, `priority`, `reporter_membership_id`, `assignee_membership_id?`, `parent_id?`, `start_date?`, `due_date?`, `estimate_minutes?`, `archived_at?`, timestamps, `version bigint` | Unique `(project_id, item_number)`; type is task or issue. Status and parent belong to the same project; reporter and assignee belong to the workspace. Nonnegative estimate. |
| `labels` / `work_item_labels` | Label: `id`, `project_id`, `name`, `color?`; join: `work_item_id`, `label_id` | Unique label name per project and unique item/label pair; both sides of join share project. |
| `work_item_dependencies` | `work_item_id`, `depends_on_item_id`, `created_at` | Unique directed pair; reject self-links and cycles. |
| `comments` / `comment_mentions` | Comment: `id`, `work_item_id`, `author_user_id`, `body`, timestamps, `deleted_at?`; mention: `comment_id`, `mentioned_user_id` | Keep author and edit/deletion audit; resolve mentions and verify access on server. |
| `attachments` | `id`, `work_item_id`, `uploaded_by_user_id`, `storage_key`, `original_name`, `content_type`, `size_bytes`, `created_at`, `deleted_at?` | Unique server-generated storage key; authorize each download. |
| `time_entries` | `id`, `work_item_id`, `user_id`, `minutes`, `started_at?`, `note?`, timestamps, `deleted_at?` | Positive minutes; preserve attribution after membership removal. |

Display an item key as `project_key-item_number` (for example `FLOW-123`). Allocate numbers atomically. Keep project keys stable after item creation unless key aliases are added. Prevent parent and dependency cycles in business logic; restrict links to one project as an initial assumption. Use optimistic concurrency for item updates and write important changes with activity/outbox rows in one transaction. Removed members remain referenced by history but cannot receive new assignments.

## Workflow and delivery

| Table | Main columns | Constraints and purpose |
| --- | --- | --- |
| `workflows` | `id`, `workspace_id`, `project_id?`, `name`, `is_enabled`, `current_version`, `created_by_user_id`, timestamps | Workspace-wide or project-scoped rule. Enable only validated configuration. |
| `workflow_versions` | `id`, `workflow_id`, `version_number`, `trigger_type`, `trigger_config jsonb`, `conditions jsonb`, `actions jsonb`, `created_at` | Unique `(workflow_id, version_number)`; immutable. Conditions use typed field/operator/value objects; actions retain order and stable IDs. |
| `domain_events` / `outbox_messages` | Event: `id`, scope IDs, `event_type`, `entity_id`, `payload jsonb`, `actor_user_id?`, `correlation_id`, `origin_execution_id?`, `depth`, `occurred_at`; outbox: `id`, `event_id`, `message_type`, `available_at`, `processed_at?`, `attempt_count` | Persist event/outbox with the business change; dispatch at least once. Correlation/origin/depth prevent workflow loops. |
| `workflow_executions` | `id`, `workflow_id`, `workflow_version_id`, `event_id`, related resource IDs, `status`, `attempt_count`, `next_attempt_at?`, `started_at?`, `finished_at?`, `safe_error?` | Unique `(workflow_id, event_id)`. Status: Pending, Running, Succeeded, Failed, Cancelled. Bound attempts and retry transient failures only. |
| `workflow_action_results` | `execution_id`, `action_id`, `status`, `attempt_count`, `safe_error?`, timestamps | Unique action per execution; pair with side-effect idempotency keys. |
| `notifications` | `id`, `recipient_user_id`, related resource IDs?, `type`, `payload jsonb`, `idempotency_key?`, `created_at`, `read_at?` | Unique idempotency key; recheck access before exposing private details. |
| `email_deliveries`* | `id`, `recipient_email`, `template_key`, `template_data jsonb`, `idempotency_key`, `status`, `attempt_count`, `next_attempt_at?` | Background delivery with bounded retry and deduplication. |
| `activity_records` | `id`, `workspace_id`, `project_id?`, `actor_user_id?`, `actor_kind`, `action`, `entity_type`, `entity_id`, `before_values jsonb?`, `after_values jsonb?`, `correlation_id?`, `workflow_execution_id?`, `occurred_at` | Append-only, authorized activity feed. Exclude secrets and attachment content. |

Workflow triggers, conditions, and actions must match the feature specification. Validate scope and current permissions for each action. `email_deliveries` is optional when email is configured. SignalR reads committed changes; Redis is only for cache/coordination or a multi-instance backplane.

## Later-phase tables

| Table | Main columns | Purpose |
| --- | --- | --- |
| `milestones` / `milestone_work_items` | Milestone: `id`, `project_id`, `name`, `target_date?`, `archived_at?`; join: `milestone_id`, `work_item_id` | Same-project association; calculate completion from linked item states. |
| `project_documents` | `id`, `project_id`, `title`, `body`, `created_by_user_id`, timestamps, `archived_at?` | Project access control; version history is optional. |

AI proposals need no table initially: validate and preview them, then create work items only after user acceptance. Reporting percentages and counts are calculated from work items rather than stored as editable fields.

## Essential indexes and checks

- Index work items by project/status, assignee, due date, and update time; index dependencies by `depends_on_item_id`, notifications by recipient/read state, and activity by workspace or project plus `occurred_at`.
- EF Core migrations must define the unique/partial indexes, foreign keys, delete behavior, and checks above. Test them against PostgreSQL, including workspace isolation, owner transfer, concurrent item-number allocation, cycle rejection, token/invitation single use, and duplicate workflow-event delivery.
