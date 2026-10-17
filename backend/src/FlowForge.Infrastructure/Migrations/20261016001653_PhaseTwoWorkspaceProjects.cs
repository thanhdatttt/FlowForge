using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FlowForge.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class PhaseTwoWorkspaceProjects : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "email_verified_at",
                table: "users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddUniqueConstraint(
                name: "AK_workspace_memberships_workspace_id_id",
                table: "workspace_memberships",
                columns: new[] { "workspace_id", "id" });

            migrationBuilder.CreateTable(
                name: "email_verification_tokens",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    normalized_email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    token_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    used_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_email_verification_tokens", x => x.id);
                    table.ForeignKey(
                        name: "FK_email_verification_tokens_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "projects",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    workspace_id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_key = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    description = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    lead_membership_id = table.Column<Guid>(type: "uuid", nullable: true),
                    starts_on = table.Column<DateOnly>(type: "date", nullable: true),
                    target_on = table.Column<DateOnly>(type: "date", nullable: true),
                    archived_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_projects", x => x.id);
                    table.UniqueConstraint("AK_projects_workspace_id_id", x => new { x.workspace_id, x.id });
                    table.CheckConstraint("CK_project_dates", "starts_on IS NULL OR target_on IS NULL OR starts_on <= target_on");
                    table.CheckConstraint("CK_project_key", "project_key ~ '^[A-Z][A-Z0-9]{1,9}$'");
                    table.ForeignKey(
                        name: "FK_projects_workspace_memberships_workspace_id_lead_membership~",
                        columns: x => new { x.workspace_id, x.lead_membership_id },
                        principalTable: "workspace_memberships",
                        principalColumns: new[] { "workspace_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_projects_workspaces_workspace_id",
                        column: x => x.workspace_id,
                        principalTable: "workspaces",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "workspace_invitations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    workspace_id = table.Column<Guid>(type: "uuid", nullable: false),
                    normalized_email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    role = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    token_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    invited_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    accepted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_workspace_invitations", x => x.id);
                    table.CheckConstraint("CK_invitation_expiry", "expires_at > created_at");
                    table.CheckConstraint("CK_invitation_role", "role IN ('Admin', 'Manager', 'Developer', 'Viewer')");
                    table.CheckConstraint("CK_invitation_terminal", "accepted_at IS NULL OR revoked_at IS NULL");
                    table.ForeignKey(
                        name: "FK_workspace_invitations_users_invited_by_user_id",
                        column: x => x.invited_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_workspace_invitations_workspaces_workspace_id",
                        column: x => x.workspace_id,
                        principalTable: "workspaces",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "project_memberships",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    workspace_id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    workspace_membership_id = table.Column<Guid>(type: "uuid", nullable: false),
                    joined_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    removed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_project_memberships", x => x.id);
                    table.ForeignKey(
                        name: "FK_project_memberships_projects_workspace_id_project_id",
                        columns: x => new { x.workspace_id, x.project_id },
                        principalTable: "projects",
                        principalColumns: new[] { "workspace_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_project_memberships_workspace_memberships_workspace_id_work~",
                        columns: x => new { x.workspace_id, x.workspace_membership_id },
                        principalTable: "workspace_memberships",
                        principalColumns: new[] { "workspace_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_email_verification_tokens_token_hash",
                table: "email_verification_tokens",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_email_verification_tokens_user_id",
                table: "email_verification_tokens",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_project_memberships_project_id_workspace_membership_id",
                table: "project_memberships",
                columns: new[] { "project_id", "workspace_membership_id" },
                unique: true,
                filter: "removed_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_project_memberships_workspace_id_project_id",
                table: "project_memberships",
                columns: new[] { "workspace_id", "project_id" });

            migrationBuilder.CreateIndex(
                name: "IX_project_memberships_workspace_id_workspace_membership_id",
                table: "project_memberships",
                columns: new[] { "workspace_id", "workspace_membership_id" });

            migrationBuilder.CreateIndex(
                name: "IX_projects_workspace_id_lead_membership_id",
                table: "projects",
                columns: new[] { "workspace_id", "lead_membership_id" });

            migrationBuilder.CreateIndex(
                name: "IX_projects_workspace_id_project_key",
                table: "projects",
                columns: new[] { "workspace_id", "project_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_workspace_invitations_invited_by_user_id",
                table: "workspace_invitations",
                column: "invited_by_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_workspace_invitations_token_hash",
                table: "workspace_invitations",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_workspace_invitations_workspace_id_normalized_email",
                table: "workspace_invitations",
                columns: new[] { "workspace_id", "normalized_email" },
                unique: true,
                filter: "accepted_at IS NULL AND revoked_at IS NULL");
            migrationBuilder.Sql("""
                CREATE FUNCTION check_workspace_owner(target uuid) RETURNS void AS $$
                BEGIN
                    PERFORM 1 FROM workspaces WHERE id = target FOR UPDATE;
                    IF FOUND AND (SELECT count(*) FROM workspace_memberships
                        WHERE workspace_id = target AND removed_at IS NULL AND role = 'Owner') <> 1 THEN
                        RAISE EXCEPTION 'A workspace must have exactly one active Owner'
                            USING ERRCODE = '23514';
                    END IF;
                END;
                $$ LANGUAGE plpgsql;

                CREATE FUNCTION enforce_workspace_owner() RETURNS trigger AS $$
                BEGIN
                    IF TG_TABLE_NAME = 'workspaces' THEN
                        PERFORM check_workspace_owner(NEW.id);
                    ELSE
                        IF TG_OP <> 'INSERT' THEN PERFORM check_workspace_owner(OLD.workspace_id); END IF;
                        IF TG_OP <> 'DELETE' THEN PERFORM check_workspace_owner(NEW.workspace_id); END IF;
                    END IF;
                    RETURN NULL;
                END;
                $$ LANGUAGE plpgsql;

                CREATE CONSTRAINT TRIGGER enforce_owner_on_workspace
                    AFTER INSERT OR UPDATE ON workspaces DEFERRABLE INITIALLY DEFERRED
                    FOR EACH ROW EXECUTE FUNCTION enforce_workspace_owner();
                CREATE CONSTRAINT TRIGGER enforce_owner_on_membership
                    AFTER INSERT OR UPDATE OR DELETE ON workspace_memberships DEFERRABLE INITIALLY DEFERRED
                    FOR EACH ROW EXECUTE FUNCTION enforce_workspace_owner();

                CREATE FUNCTION prevent_owner_deactivation() RETURNS trigger AS $$
                BEGIN
                    IF NOT NEW.is_active AND EXISTS (SELECT 1 FROM workspace_memberships
                        WHERE user_id = NEW.id AND role = 'Owner' AND removed_at IS NULL) THEN
                        RAISE EXCEPTION 'Transfer ownership before deactivating this user' USING ERRCODE = '23514';
                    END IF;
                    RETURN NEW;
                END;
                $$ LANGUAGE plpgsql;
                CREATE TRIGGER prevent_owner_deactivation BEFORE UPDATE OF is_active ON users
                    FOR EACH ROW EXECUTE FUNCTION prevent_owner_deactivation();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS enforce_owner_on_workspace ON workspaces;
                DROP TRIGGER IF EXISTS enforce_owner_on_membership ON workspace_memberships;
                DROP TRIGGER IF EXISTS prevent_owner_deactivation ON users;
                DROP FUNCTION IF EXISTS enforce_workspace_owner();
                DROP FUNCTION IF EXISTS check_workspace_owner(uuid);
                DROP FUNCTION IF EXISTS prevent_owner_deactivation();
                """);
            migrationBuilder.DropTable(
                name: "email_verification_tokens");

            migrationBuilder.DropTable(
                name: "project_memberships");

            migrationBuilder.DropTable(
                name: "workspace_invitations");

            migrationBuilder.DropTable(
                name: "projects");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_workspace_memberships_workspace_id_id",
                table: "workspace_memberships");

            migrationBuilder.DropColumn(
                name: "email_verified_at",
                table: "users");
        }
    }
}
