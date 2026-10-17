using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FlowForge.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class EmailVerificationOtp : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Long legacy tokens must not remain valid under the new OTP flow.
            // The users' email_verified_at timestamps are deliberately preserved.
            migrationBuilder.Sql("UPDATE email_verification_tokens SET used_at = CURRENT_TIMESTAMP WHERE used_at IS NULL");
            migrationBuilder.AddColumn<int>(
                name: "failed_attempts",
                table: "email_verification_tokens",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddCheckConstraint(
                name: "CK_email_verification_failed_attempts",
                table: "email_verification_tokens",
                sql: "failed_attempts BETWEEN 0 AND 5");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_email_verification_failed_attempts",
                table: "email_verification_tokens");

            migrationBuilder.DropColumn(
                name: "failed_attempts",
                table: "email_verification_tokens");
        }
    }
}
