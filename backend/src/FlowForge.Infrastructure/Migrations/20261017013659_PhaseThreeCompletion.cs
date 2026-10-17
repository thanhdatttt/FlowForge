using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FlowForge.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class PhaseThreeCompletion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "work_item_id",
                table: "activity_records",
                type: "uuid",
                nullable: true);
            migrationBuilder.Sql("""
                UPDATE activity_records a SET work_item_id = a.entity_id
                WHERE a.entity_type = 'WorkItem' AND EXISTS (
                  SELECT 1 FROM work_items w WHERE w.id = a.entity_id AND w.project_id = a.project_id);
                UPDATE activity_records a SET work_item_id = c.work_item_id
                FROM comments c WHERE a.entity_type = 'Comment' AND a.entity_id = c.id;
                UPDATE activity_records a SET work_item_id = t.work_item_id
                FROM time_entries t WHERE a.entity_type = 'TimeEntry' AND a.entity_id = t.id;
                UPDATE activity_records a SET work_item_id = f.work_item_id
                FROM attachments f WHERE a.entity_type = 'Attachment' AND a.entity_id = f.id;
                """);

            migrationBuilder.CreateTable(
                name: "domain_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    workspace_id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_type = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    correlation_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    origin_execution_id = table.Column<Guid>(type: "uuid", nullable: true),
                    depth = table.Column<int>(type: "integer", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_domain_events", x => x.id);
                    table.CheckConstraint("CK_domain_event_depth", "depth >= 0");
                    table.ForeignKey(
                        name: "FK_domain_events_projects_workspace_id_project_id",
                        columns: x => new { x.workspace_id, x.project_id },
                        principalTable: "projects",
                        principalColumns: new[] { "workspace_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_domain_events_users_actor_user_id",
                        column: x => x.actor_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "outbox_messages",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    message_type = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    available_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    processed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    attempt_count = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_outbox_messages", x => x.id);
                    table.CheckConstraint("CK_outbox_attempt_count", "attempt_count >= 0");
                    table.ForeignKey(
                        name: "FK_outbox_messages_domain_events_event_id",
                        column: x => x.event_id,
                        principalTable: "domain_events",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_activity_records_project_id_work_item_id",
                table: "activity_records",
                columns: new[] { "project_id", "work_item_id" });

            migrationBuilder.CreateIndex(
                name: "IX_activity_records_work_item_id_occurred_at",
                table: "activity_records",
                columns: new[] { "work_item_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "IX_domain_events_actor_user_id",
                table: "domain_events",
                column: "actor_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_domain_events_project_id_occurred_at",
                table: "domain_events",
                columns: new[] { "project_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "IX_domain_events_workspace_id_project_id",
                table: "domain_events",
                columns: new[] { "workspace_id", "project_id" });

            migrationBuilder.CreateIndex(
                name: "IX_outbox_messages_event_id",
                table: "outbox_messages",
                column: "event_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_outbox_messages_processed_at_available_at",
                table: "outbox_messages",
                columns: new[] { "processed_at", "available_at" });

            migrationBuilder.AddForeignKey(
                name: "FK_activity_records_work_items_project_id_work_item_id",
                table: "activity_records",
                columns: new[] { "project_id", "work_item_id" },
                principalTable: "work_items",
                principalColumns: new[] { "project_id", "id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_activity_records_work_items_project_id_work_item_id",
                table: "activity_records");

            migrationBuilder.DropTable(
                name: "outbox_messages");

            migrationBuilder.DropTable(
                name: "domain_events");

            migrationBuilder.DropIndex(
                name: "IX_activity_records_project_id_work_item_id",
                table: "activity_records");

            migrationBuilder.DropIndex(
                name: "IX_activity_records_work_item_id_occurred_at",
                table: "activity_records");

            migrationBuilder.DropColumn(
                name: "work_item_id",
                table: "activity_records");
        }
    }
}
