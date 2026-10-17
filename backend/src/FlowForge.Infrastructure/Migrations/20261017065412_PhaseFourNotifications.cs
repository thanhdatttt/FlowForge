using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FlowForge.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class PhaseFourNotifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_comments_work_item_id",
                table: "comments");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "failed_at",
                table: "outbox_messages",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "lock_id",
                table: "outbox_messages",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "safe_error",
                table: "outbox_messages",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "project_id",
                table: "domain_events",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddUniqueConstraint(
                name: "AK_workspace_invitations_workspace_id_id",
                table: "workspace_invitations",
                columns: new[] { "workspace_id", "id" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_comments_work_item_id_id",
                table: "comments",
                columns: new[] { "work_item_id", "id" });

            migrationBuilder.CreateTable(
                name: "notifications",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    recipient_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    workspace_id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: true),
                    work_item_id = table.Column<Guid>(type: "uuid", nullable: true),
                    comment_id = table.Column<Guid>(type: "uuid", nullable: true),
                    invitation_id = table.Column<Guid>(type: "uuid", nullable: true),
                    due_date = table.Column<DateOnly>(type: "date", nullable: true),
                    type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    idempotency_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    read_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_notifications", x => x.id);
                    table.UniqueConstraint("AK_notifications_workspace_id_id", x => new { x.workspace_id, x.id });
                    table.CheckConstraint("CK_notification_scope", "(project_id IS NULL OR work_item_id IS NOT NULL) AND (work_item_id IS NULL OR project_id IS NOT NULL) AND ((type = 'Invited' AND invitation_id IS NOT NULL AND project_id IS NULL) OR (type <> 'Invited' AND invitation_id IS NULL AND work_item_id IS NOT NULL))");
                    table.CheckConstraint("CK_notification_type", "type IN ('Assigned', 'Mentioned', 'Overdue', 'Invited')");
                    table.ForeignKey(
                        name: "FK_notifications_comments_work_item_id_comment_id",
                        columns: x => new { x.work_item_id, x.comment_id },
                        principalTable: "comments",
                        principalColumns: new[] { "work_item_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_notifications_projects_workspace_id_project_id",
                        columns: x => new { x.workspace_id, x.project_id },
                        principalTable: "projects",
                        principalColumns: new[] { "workspace_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_notifications_users_recipient_user_id",
                        column: x => x.recipient_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_notifications_work_items_project_id_work_item_id",
                        columns: x => new { x.project_id, x.work_item_id },
                        principalTable: "work_items",
                        principalColumns: new[] { "project_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_notifications_workspace_invitations_workspace_id_invitation~",
                        columns: x => new { x.workspace_id, x.invitation_id },
                        principalTable: "workspace_invitations",
                        principalColumns: new[] { "workspace_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_notifications_workspaces_workspace_id",
                        column: x => x.workspace_id,
                        principalTable: "workspaces",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "email_deliveries",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    workspace_id = table.Column<Guid>(type: "uuid", nullable: false),
                    notification_id = table.Column<Guid>(type: "uuid", nullable: true),
                    invitation_id = table.Column<Guid>(type: "uuid", nullable: true),
                    recipient_email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    subject = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    protected_body = table.Column<string>(type: "text", nullable: false),
                    idempotency_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    attempt_count = table.Column<int>(type: "integer", nullable: false),
                    next_attempt_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    lock_id = table.Column<Guid>(type: "uuid", nullable: true),
                    safe_error = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_email_deliveries", x => x.id);
                    table.CheckConstraint("CK_email_delivery_attempts", "attempt_count >= 0");
                    table.CheckConstraint("CK_email_delivery_status", "status IN ('Pending', 'Sent', 'Cancelled', 'Failed')");
                    table.CheckConstraint("CK_email_delivery_target", "(notification_id IS NULL) <> (invitation_id IS NULL)");
                    table.ForeignKey(
                        name: "FK_email_deliveries_notifications_workspace_id_notification_id",
                        columns: x => new { x.workspace_id, x.notification_id },
                        principalTable: "notifications",
                        principalColumns: new[] { "workspace_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_email_deliveries_workspace_invitations_workspace_id_invitat~",
                        columns: x => new { x.workspace_id, x.invitation_id },
                        principalTable: "workspace_invitations",
                        principalColumns: new[] { "workspace_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_email_deliveries_workspaces_workspace_id",
                        column: x => x.workspace_id,
                        principalTable: "workspaces",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_email_deliveries_idempotency_key",
                table: "email_deliveries",
                column: "idempotency_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_email_deliveries_status_next_attempt_at",
                table: "email_deliveries",
                columns: new[] { "status", "next_attempt_at" });

            migrationBuilder.CreateIndex(
                name: "IX_email_deliveries_workspace_id_invitation_id",
                table: "email_deliveries",
                columns: new[] { "workspace_id", "invitation_id" });

            migrationBuilder.CreateIndex(
                name: "IX_email_deliveries_workspace_id_notification_id",
                table: "email_deliveries",
                columns: new[] { "workspace_id", "notification_id" });

            migrationBuilder.CreateIndex(
                name: "IX_notifications_idempotency_key",
                table: "notifications",
                column: "idempotency_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_notifications_project_id_work_item_id",
                table: "notifications",
                columns: new[] { "project_id", "work_item_id" });

            migrationBuilder.CreateIndex(
                name: "IX_notifications_recipient_user_id_read_at_created_at",
                table: "notifications",
                columns: new[] { "recipient_user_id", "read_at", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_notifications_work_item_id_comment_id",
                table: "notifications",
                columns: new[] { "work_item_id", "comment_id" });

            migrationBuilder.CreateIndex(
                name: "IX_notifications_work_item_id_due_date_recipient_user_id",
                table: "notifications",
                columns: new[] { "work_item_id", "due_date", "recipient_user_id" });

            migrationBuilder.CreateIndex(
                name: "IX_notifications_workspace_id_invitation_id",
                table: "notifications",
                columns: new[] { "workspace_id", "invitation_id" });

            migrationBuilder.CreateIndex(
                name: "IX_notifications_workspace_id_project_id",
                table: "notifications",
                columns: new[] { "workspace_id", "project_id" });

            migrationBuilder.AddForeignKey(
                name: "FK_domain_events_workspaces_workspace_id",
                table: "domain_events",
                column: "workspace_id",
                principalTable: "workspaces",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Phase 3 cannot represent workspace-level events. Preserve event history rather
            // than silently replacing its scope or deleting invitation audit information.
            migrationBuilder.Sql("""
                DO $$ BEGIN
                    IF EXISTS (SELECT 1 FROM domain_events WHERE project_id IS NULL) THEN
                        RAISE EXCEPTION 'Cannot downgrade to Phase 3 while workspace-level domain events exist.';
                    END IF;
                END $$;
                """);
            migrationBuilder.DropForeignKey(
                name: "FK_domain_events_workspaces_workspace_id",
                table: "domain_events");

            migrationBuilder.DropTable(
                name: "email_deliveries");

            migrationBuilder.DropTable(
                name: "notifications");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_workspace_invitations_workspace_id_id",
                table: "workspace_invitations");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_comments_work_item_id_id",
                table: "comments");

            migrationBuilder.DropColumn(
                name: "failed_at",
                table: "outbox_messages");

            migrationBuilder.DropColumn(
                name: "lock_id",
                table: "outbox_messages");

            migrationBuilder.DropColumn(
                name: "safe_error",
                table: "outbox_messages");

            migrationBuilder.AlterColumn<Guid>(
                name: "project_id",
                table: "domain_events",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_comments_work_item_id",
                table: "comments",
                column: "work_item_id");
        }
    }
}
