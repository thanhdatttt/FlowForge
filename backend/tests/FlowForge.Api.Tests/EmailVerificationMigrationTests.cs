using FlowForge.Domain.Identity;
using FlowForge.Infrastructure;
using FlowForge.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Xunit;

namespace FlowForge.Api.Tests;

public sealed class EmailVerificationMigrationTests
{
    [PostgresFact]
    public async Task Otp_upgrade_expires_legacy_tokens_and_preserves_previously_verified_accounts()
    {
        var configured = Environment.GetEnvironmentVariable("FLOWFORGE_TEST_CONNECTION")!;
        var database = "flowforge_otp_upgrade_" + Guid.NewGuid().ToString("N");
        await using var admin = new NpgsqlConnection(configured);
        await admin.OpenAsync();
        await using (var create = new NpgsqlCommand($"CREATE DATABASE {database}", admin))
            await create.ExecuteNonQueryAsync();
        try
        {
            var settings = new NpgsqlConnectionStringBuilder(configured) { Database = database };
            await using var db = new FlowForgeDbContext(new DbContextOptionsBuilder<FlowForgeDbContext>()
                .UseNpgsql(settings.ConnectionString).Options);
            var previousMigration = db.Database.GetMigrations().Single(x => x.EndsWith("_PhaseTwoWorkspaceProjects"));
            await db.GetService<IMigrator>().MigrateAsync(previousMigration);
            var verifiedAt = new DateTimeOffset(2026, 10, 16, 0, 0, 0, TimeSpan.Zero);
            var user = new User { Email = "upgrade@example.com", NormalizedEmail = "UPGRADE@EXAMPLE.COM",
                DisplayName = "Upgrade user", PasswordHash = "test-only", EmailVerifiedAt = verifiedAt };
            db.Users.Add(user);
            await db.SaveChangesAsync();
            var id = Guid.NewGuid();
            var hash = SecureTokens.Hash("legacy-long-verification-token");
            var created = DateTimeOffset.UtcNow;
            var expires = created.AddDays(1);
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO email_verification_tokens (id, user_id, normalized_email, token_hash, created_at, expires_at)
                VALUES ({id}, {user.Id}, {user.NormalizedEmail}, {hash}, {created}, {expires})
                """);
            await db.Database.MigrateAsync();
            db.ChangeTracker.Clear();
            Assert.Equal(verifiedAt, (await db.Users.SingleAsync()).EmailVerifiedAt);
            var challenge = await db.EmailVerificationTokens.SingleAsync();
            Assert.NotNull(challenge.UsedAt);
            Assert.Equal(0, challenge.FailedAttempts);
        }
        finally
        {
            await using var drop = new NpgsqlCommand($"DROP DATABASE {database} WITH (FORCE)", admin);
            await drop.ExecuteNonQueryAsync();
        }
    }
}
