using FlowForge.Domain.Identity;
using FlowForge.Domain.Workspaces;
using FlowForge.Domain.Workflows;
using FlowForge.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Xunit;

namespace FlowForge.Api.Tests;

public sealed class PhaseFiveMigrationTests
{
    [PostgresFact]
    public async Task Phase_four_upgrade_preserves_notification_outbox_and_checkpoints_history_without_replay()
    {
        var configured = Environment.GetEnvironmentVariable("FLOWFORGE_TEST_CONNECTION")!;
        var database = "flowforge_phase5_upgrade_" + Guid.NewGuid().ToString("N");
        await using var admin = new NpgsqlConnection(configured); await admin.OpenAsync();
        await using (var create = new NpgsqlCommand($"CREATE DATABASE {database}", admin)) await create.ExecuteNonQueryAsync();
        try
        {
            var settings = new NpgsqlConnectionStringBuilder(configured) { Database = database };
            await using var db = new FlowForgeDbContext(new DbContextOptionsBuilder<FlowForgeDbContext>().UseNpgsql(settings.ConnectionString).Options);
            var migrator = db.GetService<IMigrator>();
            await migrator.MigrateAsync(db.Database.GetMigrations().Single(x => x.EndsWith("_PhaseFourNotifications")));
            var user = new User { Email = "migration@example.com", NormalizedEmail = "MIGRATION@EXAMPLE.COM", DisplayName = "Upgrade", PasswordHash = "test" };
            var workspace = new Workspace { Name = "Upgrade", CreatedByUserId = user.Id };
            db.Users.Add(user); db.Workspaces.Add(workspace);
            db.WorkspaceMemberships.Add(new WorkspaceMembership { WorkspaceId = workspace.Id, UserId = user.Id, Role = WorkspaceRole.Owner });
            await db.SaveChangesAsync();
            var eventId = Guid.NewGuid(); var outboxId = Guid.NewGuid(); var payload = "{}";
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO domain_events (id, workspace_id, event_type, entity_id, payload, correlation_id, depth, occurred_at)
                VALUES ({eventId}, {workspace.Id}, 'InvitationCreated', {Guid.NewGuid()}, {payload}::jsonb, 'legacy', 0, {DateTimeOffset.UtcNow});
                INSERT INTO outbox_messages (id, event_id, message_type, available_at, attempt_count)
                VALUES ({outboxId}, {eventId}, 'DomainEvent', {DateTimeOffset.UtcNow}, 0);
                """);
            await db.Database.MigrateAsync(); db.ChangeTracker.Clear();
            Assert.NotNull((await db.DomainEvents.SingleAsync(x => x.Id == eventId)).WorkflowProcessedAt);
            var message = await db.OutboxMessages.SingleAsync(x => x.Id == outboxId);
            Assert.Null(message.ProcessedAt); Assert.Equal(0, message.AttemptCount);
            Assert.Empty(await db.Workflows.ToListAsync()); Assert.False(db.Database.HasPendingModelChanges());
            var workflow = new Workflow { WorkspaceId = workspace.Id, Name = "Immutable", CreatedByUserId = user.Id };
            var version = new WorkflowVersion { WorkflowId = workflow.Id, VersionNumber = 1, TriggerType = "WorkItemCreated", Configuration = "{}" };
            db.Workflows.Add(workflow); db.WorkflowVersions.Add(version); await db.SaveChangesAsync();
            version.Configuration = "{\"changed\":true}";
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync()); db.ChangeTracker.Clear();
            await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM workflow_versions WHERE id = {version.Id}"));
            db.WorkflowVersions.Add(new WorkflowVersion { WorkflowId = workflow.Id, VersionNumber = 1, TriggerType = "CommentAdded", Configuration = "{}" });
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync()); db.ChangeTracker.Clear();
            db.WorkflowVersions.Add(new WorkflowVersion { WorkflowId = workflow.Id, VersionNumber = 2, TriggerType = "ExecuteCode", Configuration = "{}" });
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync()); db.ChangeTracker.Clear();
            await Assert.ThrowsAsync<PostgresException>(() => migrator.MigrateAsync(db.Database.GetMigrations().Single(x => x.EndsWith("_PhaseFourNotifications"))));
        }
        finally
        {
            NpgsqlConnection.ClearAllPools();
            await using var drop = new NpgsqlCommand($"DROP DATABASE {database} WITH (FORCE)", admin); await drop.ExecuteNonQueryAsync();
        }
    }
}
