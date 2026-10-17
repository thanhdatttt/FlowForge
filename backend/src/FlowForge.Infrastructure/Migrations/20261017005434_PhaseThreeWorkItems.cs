using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FlowForge.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class PhaseThreeWorkItems : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "next_item_number",
                table: "projects",
                type: "bigint",
                nullable: false,
                defaultValue: 1L);

            migrationBuilder.CreateTable(
                name: "activity_records",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    workspace_id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: true),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    actor_kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    action = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    entity_type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    before_values = table.Column<string>(type: "jsonb", nullable: true),
                    after_values = table.Column<string>(type: "jsonb", nullable: true),
                    correlation_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_activity_records", x => x.id);
                    table.ForeignKey(
                        name: "FK_activity_records_projects_workspace_id_project_id",
                        columns: x => new { x.workspace_id, x.project_id },
                        principalTable: "projects",
                        principalColumns: new[] { "workspace_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_activity_records_users_actor_user_id",
                        column: x => x.actor_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_activity_records_workspaces_workspace_id",
                        column: x => x.workspace_id,
                        principalTable: "workspaces",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "labels",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    color = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_labels", x => x.id);
                    table.UniqueConstraint("AK_labels_project_id_id", x => new { x.project_id, x.id });
                    table.ForeignKey(
                        name: "FK_labels_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "project_statuses",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    category = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_project_statuses", x => x.id);
                    table.UniqueConstraint("AK_project_statuses_project_id_id", x => new { x.project_id, x.id });
                    table.CheckConstraint("CK_status_category", "category IN ('Open', 'Done')");
                    table.ForeignKey(
                        name: "FK_project_statuses_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "work_items",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    workspace_id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_number = table.Column<long>(type: "bigint", nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "character varying(20000)", maxLength: 20000, nullable: true),
                    type = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    status_id = table.Column<Guid>(type: "uuid", nullable: false),
                    priority = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    reporter_membership_id = table.Column<Guid>(type: "uuid", nullable: false),
                    assignee_membership_id = table.Column<Guid>(type: "uuid", nullable: true),
                    parent_id = table.Column<Guid>(type: "uuid", nullable: true),
                    start_date = table.Column<DateOnly>(type: "date", nullable: true),
                    due_date = table.Column<DateOnly>(type: "date", nullable: true),
                    estimate_minutes = table.Column<int>(type: "integer", nullable: true),
                    archived_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_work_items", x => x.id);
                    table.UniqueConstraint("AK_work_items_project_id_id", x => new { x.project_id, x.id });
                    table.CheckConstraint("CK_work_item_dates", "start_date IS NULL OR due_date IS NULL OR start_date <= due_date");
                    table.CheckConstraint("CK_work_item_estimate", "estimate_minutes IS NULL OR estimate_minutes >= 0");
                    table.CheckConstraint("CK_work_item_number", "item_number > 0");
                    table.CheckConstraint("CK_work_item_parent", "parent_id IS NULL OR parent_id <> id");
                    table.CheckConstraint("CK_work_item_priority", "priority IN ('Low', 'Medium', 'High', 'Critical')");
                    table.CheckConstraint("CK_work_item_type", "type IN ('Task', 'Issue')");
                    table.ForeignKey(
                        name: "FK_work_items_project_statuses_project_id_status_id",
                        columns: x => new { x.project_id, x.status_id },
                        principalTable: "project_statuses",
                        principalColumns: new[] { "project_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_work_items_projects_workspace_id_project_id",
                        columns: x => new { x.workspace_id, x.project_id },
                        principalTable: "projects",
                        principalColumns: new[] { "workspace_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_work_items_work_items_project_id_parent_id",
                        columns: x => new { x.project_id, x.parent_id },
                        principalTable: "work_items",
                        principalColumns: new[] { "project_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_work_items_workspace_memberships_workspace_id_assignee_memb~",
                        columns: x => new { x.workspace_id, x.assignee_membership_id },
                        principalTable: "workspace_memberships",
                        principalColumns: new[] { "workspace_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_work_items_workspace_memberships_workspace_id_reporter_memb~",
                        columns: x => new { x.workspace_id, x.reporter_membership_id },
                        principalTable: "workspace_memberships",
                        principalColumns: new[] { "workspace_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "attachments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    work_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    uploaded_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    storage_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    original_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    content_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_attachments", x => x.id);
                    table.CheckConstraint("CK_attachment_size", "size_bytes > 0");
                    table.ForeignKey(
                        name: "FK_attachments_users_uploaded_by_user_id",
                        column: x => x.uploaded_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_attachments_work_items_work_item_id",
                        column: x => x.work_item_id,
                        principalTable: "work_items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "comments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    work_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    author_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    body = table.Column<string>(type: "character varying(10000)", maxLength: 10000, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_comments", x => x.id);
                    table.ForeignKey(
                        name: "FK_comments_users_author_user_id",
                        column: x => x.author_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_comments_work_items_work_item_id",
                        column: x => x.work_item_id,
                        principalTable: "work_items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "time_entries",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    work_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    minutes = table.Column<int>(type: "integer", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    note = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_time_entries", x => x.id);
                    table.CheckConstraint("CK_time_minutes", "minutes > 0");
                    table.ForeignKey(
                        name: "FK_time_entries_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_time_entries_work_items_work_item_id",
                        column: x => x.work_item_id,
                        principalTable: "work_items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "work_item_dependencies",
                columns: table => new
                {
                    work_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    depends_on_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_work_item_dependencies", x => new { x.work_item_id, x.depends_on_item_id });
                    table.CheckConstraint("CK_dependency_self", "work_item_id <> depends_on_item_id");
                    table.ForeignKey(
                        name: "FK_work_item_dependencies_work_items_project_id_depends_on_ite~",
                        columns: x => new { x.project_id, x.depends_on_item_id },
                        principalTable: "work_items",
                        principalColumns: new[] { "project_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_work_item_dependencies_work_items_project_id_work_item_id",
                        columns: x => new { x.project_id, x.work_item_id },
                        principalTable: "work_items",
                        principalColumns: new[] { "project_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "work_item_labels",
                columns: table => new
                {
                    work_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    label_id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_work_item_labels", x => new { x.work_item_id, x.label_id });
                    table.ForeignKey(
                        name: "FK_work_item_labels_labels_project_id_label_id",
                        columns: x => new { x.project_id, x.label_id },
                        principalTable: "labels",
                        principalColumns: new[] { "project_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_work_item_labels_work_items_project_id_work_item_id",
                        columns: x => new { x.project_id, x.work_item_id },
                        principalTable: "work_items",
                        principalColumns: new[] { "project_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "comment_mentions",
                columns: table => new
                {
                    comment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    mentioned_user_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_comment_mentions", x => new { x.comment_id, x.mentioned_user_id });
                    table.ForeignKey(
                        name: "FK_comment_mentions_comments_comment_id",
                        column: x => x.comment_id,
                        principalTable: "comments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_comment_mentions_users_mentioned_user_id",
                        column: x => x.mentioned_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_activity_records_actor_user_id",
                table: "activity_records",
                column: "actor_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_activity_records_project_id_occurred_at",
                table: "activity_records",
                columns: new[] { "project_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "IX_activity_records_workspace_id_occurred_at",
                table: "activity_records",
                columns: new[] { "workspace_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "IX_activity_records_workspace_id_project_id",
                table: "activity_records",
                columns: new[] { "workspace_id", "project_id" });

            migrationBuilder.CreateIndex(
                name: "IX_attachments_storage_key",
                table: "attachments",
                column: "storage_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_attachments_uploaded_by_user_id",
                table: "attachments",
                column: "uploaded_by_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_attachments_work_item_id",
                table: "attachments",
                column: "work_item_id");

            migrationBuilder.CreateIndex(
                name: "IX_comment_mentions_mentioned_user_id",
                table: "comment_mentions",
                column: "mentioned_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_comments_author_user_id",
                table: "comments",
                column: "author_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_comments_work_item_id",
                table: "comments",
                column: "work_item_id");

            migrationBuilder.CreateIndex(
                name: "IX_labels_project_id_name",
                table: "labels",
                columns: new[] { "project_id", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_project_statuses_project_id_name",
                table: "project_statuses",
                columns: new[] { "project_id", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_time_entries_user_id",
                table: "time_entries",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_time_entries_work_item_id",
                table: "time_entries",
                column: "work_item_id");

            migrationBuilder.CreateIndex(
                name: "IX_work_item_dependencies_depends_on_item_id",
                table: "work_item_dependencies",
                column: "depends_on_item_id");

            migrationBuilder.CreateIndex(
                name: "IX_work_item_dependencies_project_id_depends_on_item_id",
                table: "work_item_dependencies",
                columns: new[] { "project_id", "depends_on_item_id" });

            migrationBuilder.CreateIndex(
                name: "IX_work_item_dependencies_project_id_work_item_id",
                table: "work_item_dependencies",
                columns: new[] { "project_id", "work_item_id" });

            migrationBuilder.CreateIndex(
                name: "IX_work_item_labels_project_id_label_id",
                table: "work_item_labels",
                columns: new[] { "project_id", "label_id" });

            migrationBuilder.CreateIndex(
                name: "IX_work_item_labels_project_id_work_item_id",
                table: "work_item_labels",
                columns: new[] { "project_id", "work_item_id" });

            migrationBuilder.CreateIndex(
                name: "IX_work_items_project_id_assignee_membership_id",
                table: "work_items",
                columns: new[] { "project_id", "assignee_membership_id" });

            migrationBuilder.CreateIndex(
                name: "IX_work_items_project_id_due_date",
                table: "work_items",
                columns: new[] { "project_id", "due_date" });

            migrationBuilder.CreateIndex(
                name: "IX_work_items_project_id_item_number",
                table: "work_items",
                columns: new[] { "project_id", "item_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_work_items_project_id_parent_id",
                table: "work_items",
                columns: new[] { "project_id", "parent_id" });

            migrationBuilder.CreateIndex(
                name: "IX_work_items_project_id_status_id",
                table: "work_items",
                columns: new[] { "project_id", "status_id" });

            migrationBuilder.CreateIndex(
                name: "IX_work_items_project_id_updated_at",
                table: "work_items",
                columns: new[] { "project_id", "updated_at" });

            migrationBuilder.CreateIndex(
                name: "IX_work_items_workspace_id_assignee_membership_id",
                table: "work_items",
                columns: new[] { "workspace_id", "assignee_membership_id" });

            migrationBuilder.CreateIndex(
                name: "IX_work_items_workspace_id_project_id",
                table: "work_items",
                columns: new[] { "workspace_id", "project_id" });

            migrationBuilder.CreateIndex(
                name: "IX_work_items_workspace_id_reporter_membership_id",
                table: "work_items",
                columns: new[] { "workspace_id", "reporter_membership_id" });
            migrationBuilder.Sql("""
                INSERT INTO project_statuses (id, project_id, name, category, sort_order, is_active)
                SELECT gen_random_uuid(), p.id, s.name, s.category, s.sort_order, TRUE
                FROM projects p CROSS JOIN (VALUES
                  ('Todo', 'Open', 0), ('InProgress', 'Open', 1), ('InReview', 'Open', 2),
                  ('Done', 'Done', 3), ('Blocked', 'Open', 4)
                ) AS s(name, category, sort_order);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "activity_records");

            migrationBuilder.DropTable(
                name: "attachments");

            migrationBuilder.DropTable(
                name: "comment_mentions");

            migrationBuilder.DropTable(
                name: "time_entries");

            migrationBuilder.DropTable(
                name: "work_item_dependencies");

            migrationBuilder.DropTable(
                name: "work_item_labels");

            migrationBuilder.DropTable(
                name: "comments");

            migrationBuilder.DropTable(
                name: "labels");

            migrationBuilder.DropTable(
                name: "work_items");

            migrationBuilder.DropTable(
                name: "project_statuses");

            migrationBuilder.DropColumn(
                name: "next_item_number",
                table: "projects");
        }
    }
}
