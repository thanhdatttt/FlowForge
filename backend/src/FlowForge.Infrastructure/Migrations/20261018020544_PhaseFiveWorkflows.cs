using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FlowForge.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class PhaseFiveWorkflows : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_notification_type",
                table: "notifications");

            migrationBuilder.DropCheckConstraint(
                name: "CK_email_delivery_target",
                table: "email_deliveries");

            migrationBuilder.AddColumn<Guid>(
                name: "recipient_user_id",
                table: "email_deliveries",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "workflow_execution_id",
                table: "email_deliveries",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "deduplication_key",
                table: "domain_events",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "trigger_date",
                table: "domain_events",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "workflow_processed_at",
                table: "domain_events",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "workflow_execution_id",
                table: "activity_records",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddUniqueConstraint(
                name: "AK_domain_events_workspace_id_id",
                table: "domain_events",
                columns: new[] { "workspace_id", "id" });

            migrationBuilder.CreateTable(
                name: "workflows",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    workspace_id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: true),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    is_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    enabled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    current_version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_workflows", x => x.id);
                    table.UniqueConstraint("AK_workflows_workspace_id_id", x => new { x.workspace_id, x.id });
                    table.CheckConstraint("CK_workflow_enabled", "NOT is_enabled OR enabled_at IS NOT NULL");
                    table.CheckConstraint("CK_workflow_version", "current_version > 0");
                    table.ForeignKey(
                        name: "FK_workflows_projects_workspace_id_project_id",
                        columns: x => new { x.workspace_id, x.project_id },
                        principalTable: "projects",
                        principalColumns: new[] { "workspace_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_workflows_users_created_by_user_id",
                        column: x => x.created_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_workflows_workspaces_workspace_id",
                        column: x => x.workspace_id,
                        principalTable: "workspaces",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "workflow_versions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    workflow_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version_number = table.Column<int>(type: "integer", nullable: false),
                    trigger_type = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    configuration = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_workflow_versions", x => x.id);
                    table.UniqueConstraint("AK_workflow_versions_workflow_id_id", x => new { x.workflow_id, x.id });
                    table.CheckConstraint("CK_workflow_trigger", "trigger_type IN ('WorkItemCreated', 'WorkItemUpdated', 'WorkItemStatusChanged', 'WorkItemAssignmentChanged', 'CommentAdded', 'WorkItemDueDateReached', 'WorkItemDueDateOverdue')");
                    table.CheckConstraint("CK_workflow_version_number", "version_number > 0");
                    table.ForeignKey(
                        name: "FK_workflow_versions_workflows_workflow_id",
                        column: x => x.workflow_id,
                        principalTable: "workflows",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "workflow_executions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    workspace_id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    work_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    workflow_id = table.Column<Guid>(type: "uuid", nullable: false),
                    workflow_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    conditions_matched = table.Column<bool>(type: "boolean", nullable: true),
                    attempt_count = table.Column<int>(type: "integer", nullable: false),
                    lock_id = table.Column<Guid>(type: "uuid", nullable: true),
                    next_attempt_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    finished_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    safe_error = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_workflow_executions", x => x.id);
                    table.UniqueConstraint("AK_workflow_executions_workspace_id_id", x => new { x.workspace_id, x.id });
                    table.CheckConstraint("CK_workflow_execution_attempts", "attempt_count >= 0");
                    table.CheckConstraint("CK_workflow_execution_status", "status IN ('Pending', 'Running', 'Succeeded', 'Failed', 'Cancelled')");
                    table.ForeignKey(
                        name: "FK_workflow_executions_domain_events_workspace_id_event_id",
                        columns: x => new { x.workspace_id, x.event_id },
                        principalTable: "domain_events",
                        principalColumns: new[] { "workspace_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_workflow_executions_projects_workspace_id_project_id",
                        columns: x => new { x.workspace_id, x.project_id },
                        principalTable: "projects",
                        principalColumns: new[] { "workspace_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_workflow_executions_work_items_project_id_work_item_id",
                        columns: x => new { x.project_id, x.work_item_id },
                        principalTable: "work_items",
                        principalColumns: new[] { "project_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_workflow_executions_workflow_versions_workflow_id_workflow_~",
                        columns: x => new { x.workflow_id, x.workflow_version_id },
                        principalTable: "workflow_versions",
                        principalColumns: new[] { "workflow_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_workflow_executions_workflows_workspace_id_workflow_id",
                        columns: x => new { x.workspace_id, x.workflow_id },
                        principalTable: "workflows",
                        principalColumns: new[] { "workspace_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "workflow_action_results",
                columns: table => new
                {
                    execution_id = table.Column<Guid>(type: "uuid", nullable: false),
                    action_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    attempt_count = table.Column<int>(type: "integer", nullable: false),
                    safe_error = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    finished_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_workflow_action_results", x => new { x.execution_id, x.action_id });
                    table.CheckConstraint("CK_workflow_action_attempts", "attempt_count >= 0");
                    table.CheckConstraint("CK_workflow_action_status", "status IN ('Pending', 'Succeeded', 'Failed', 'Cancelled')");
                    table.ForeignKey(
                        name: "FK_workflow_action_results_workflow_executions_execution_id",
                        column: x => x.execution_id,
                        principalTable: "workflow_executions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_notification_type",
                table: "notifications",
                sql: "type IN ('Assigned', 'Mentioned', 'Overdue', 'Invited', 'Workflow')");

            migrationBuilder.CreateIndex(
                name: "IX_email_deliveries_recipient_user_id",
                table: "email_deliveries",
                column: "recipient_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_email_deliveries_workspace_id_workflow_execution_id",
                table: "email_deliveries",
                columns: new[] { "workspace_id", "workflow_execution_id" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_email_delivery_target",
                table: "email_deliveries",
                sql: "num_nonnulls(notification_id, invitation_id, workflow_execution_id) = 1");

            migrationBuilder.CreateIndex(
                name: "IX_domain_events_deduplication_key",
                table: "domain_events",
                column: "deduplication_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_domain_events_workflow_processed_at_occurred_at",
                table: "domain_events",
                columns: new[] { "workflow_processed_at", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "IX_activity_records_workspace_id_workflow_execution_id",
                table: "activity_records",
                columns: new[] { "workspace_id", "workflow_execution_id" });

            migrationBuilder.CreateIndex(
                name: "IX_workflow_executions_project_id_work_item_id",
                table: "workflow_executions",
                columns: new[] { "project_id", "work_item_id" });

            migrationBuilder.CreateIndex(
                name: "IX_workflow_executions_status_next_attempt_at",
                table: "workflow_executions",
                columns: new[] { "status", "next_attempt_at" });

            migrationBuilder.CreateIndex(
                name: "IX_workflow_executions_workflow_id_created_at",
                table: "workflow_executions",
                columns: new[] { "workflow_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_workflow_executions_workflow_id_event_id",
                table: "workflow_executions",
                columns: new[] { "workflow_id", "event_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_workflow_executions_workflow_id_workflow_version_id",
                table: "workflow_executions",
                columns: new[] { "workflow_id", "workflow_version_id" });

            migrationBuilder.CreateIndex(
                name: "IX_workflow_executions_workspace_id_event_id",
                table: "workflow_executions",
                columns: new[] { "workspace_id", "event_id" });

            migrationBuilder.CreateIndex(
                name: "IX_workflow_executions_workspace_id_project_id",
                table: "workflow_executions",
                columns: new[] { "workspace_id", "project_id" });

            migrationBuilder.CreateIndex(
                name: "IX_workflow_executions_workspace_id_workflow_id",
                table: "workflow_executions",
                columns: new[] { "workspace_id", "workflow_id" });

            migrationBuilder.CreateIndex(
                name: "IX_workflow_versions_workflow_id_version_number",
                table: "workflow_versions",
                columns: new[] { "workflow_id", "version_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_workflows_created_by_user_id",
                table: "workflows",
                column: "created_by_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_workflows_workspace_id_project_id_is_enabled",
                table: "workflows",
                columns: new[] { "workspace_id", "project_id", "is_enabled" });

            migrationBuilder.AddForeignKey(
                name: "FK_activity_records_workflow_executions_workspace_id_workflow_~",
                table: "activity_records",
                columns: new[] { "workspace_id", "workflow_execution_id" },
                principalTable: "workflow_executions",
                principalColumns: new[] { "workspace_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_email_deliveries_users_recipient_user_id",
                table: "email_deliveries",
                column: "recipient_user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_email_deliveries_workflow_executions_workspace_id_workflow_~",
                table: "email_deliveries",
                columns: new[] { "workspace_id", "workflow_execution_id" },
                principalTable: "workflow_executions",
                principalColumns: new[] { "workspace_id", "id" },
                onDelete: ReferentialAction.Restrict);
            // Do not replay pre-upgrade business history into newly introduced workflows.
            migrationBuilder.Sql("UPDATE domain_events SET workflow_processed_at = CURRENT_TIMESTAMP");
            migrationBuilder.Sql("""
                CREATE FUNCTION protect_workflow_version() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    RAISE EXCEPTION 'Workflow versions are immutable' USING ERRCODE = '23514';
                END $$;
                CREATE TRIGGER workflow_versions_immutable BEFORE UPDATE OR DELETE ON workflow_versions
                FOR EACH ROW EXECUTE FUNCTION protect_workflow_version();
                """);

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$ BEGIN
                    IF EXISTS (SELECT 1 FROM workflows) THEN
                        RAISE EXCEPTION 'Downgrade would discard workflow history; restore a pre-upgrade backup instead';
                    END IF;
                END $$;
                DROP TRIGGER workflow_versions_immutable ON workflow_versions;
                DROP FUNCTION protect_workflow_version();
                """);
            migrationBuilder.DropForeignKey(
                name: "FK_activity_records_workflow_executions_workspace_id_workflow_~",
                table: "activity_records");

            migrationBuilder.DropForeignKey(
                name: "FK_email_deliveries_users_recipient_user_id",
                table: "email_deliveries");

            migrationBuilder.DropForeignKey(
                name: "FK_email_deliveries_workflow_executions_workspace_id_workflow_~",
                table: "email_deliveries");

            migrationBuilder.DropTable(
                name: "workflow_action_results");

            migrationBuilder.DropTable(
                name: "workflow_executions");

            migrationBuilder.DropTable(
                name: "workflow_versions");

            migrationBuilder.DropTable(
                name: "workflows");

            migrationBuilder.DropCheckConstraint(
                name: "CK_notification_type",
                table: "notifications");

            migrationBuilder.DropIndex(
                name: "IX_email_deliveries_recipient_user_id",
                table: "email_deliveries");

            migrationBuilder.DropIndex(
                name: "IX_email_deliveries_workspace_id_workflow_execution_id",
                table: "email_deliveries");

            migrationBuilder.DropCheckConstraint(
                name: "CK_email_delivery_target",
                table: "email_deliveries");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_domain_events_workspace_id_id",
                table: "domain_events");

            migrationBuilder.DropIndex(
                name: "IX_domain_events_deduplication_key",
                table: "domain_events");

            migrationBuilder.DropIndex(
                name: "IX_domain_events_workflow_processed_at_occurred_at",
                table: "domain_events");

            migrationBuilder.DropIndex(
                name: "IX_activity_records_workspace_id_workflow_execution_id",
                table: "activity_records");

            migrationBuilder.DropColumn(
                name: "recipient_user_id",
                table: "email_deliveries");

            migrationBuilder.DropColumn(
                name: "workflow_execution_id",
                table: "email_deliveries");

            migrationBuilder.DropColumn(
                name: "deduplication_key",
                table: "domain_events");

            migrationBuilder.DropColumn(
                name: "trigger_date",
                table: "domain_events");

            migrationBuilder.DropColumn(
                name: "workflow_processed_at",
                table: "domain_events");

            migrationBuilder.DropColumn(
                name: "workflow_execution_id",
                table: "activity_records");

            migrationBuilder.AddCheckConstraint(
                name: "CK_notification_type",
                table: "notifications",
                sql: "type IN ('Assigned', 'Mentioned', 'Overdue', 'Invited')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_email_delivery_target",
                table: "email_deliveries",
                sql: "(notification_id IS NULL) <> (invitation_id IS NULL)");
        }
    }
}
