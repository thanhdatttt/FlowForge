using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FlowForge.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class PasswordResetOtp : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "password_reset_challenges",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    normalized_email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    code_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    failed_attempts = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    confirmed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    reset_token_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    reset_token_expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    used_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_password_reset_challenges", x => x.id);
                    table.CheckConstraint("CK_password_reset_failed_attempts", "failed_attempts BETWEEN 0 AND 5");
                    table.ForeignKey(
                        name: "FK_password_reset_challenges_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_password_reset_challenges_reset_token_hash",
                table: "password_reset_challenges",
                column: "reset_token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_password_reset_challenges_user_id_created_at",
                table: "password_reset_challenges",
                columns: new[] { "user_id", "created_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "password_reset_challenges");
        }
    }
}
