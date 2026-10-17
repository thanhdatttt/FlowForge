using System.Text.Json;
using FlowForge.Domain.Identity;
using FlowForge.Domain.Notifications;
using FlowForge.Domain.Projects;
using FlowForge.Domain.WorkItems;
using FlowForge.Domain.Workspaces;
using FlowForge.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Xunit;

namespace FlowForge.Api.Tests;

public sealed class PhaseFourMigrationTests
{
    [PostgresFact]
    public async Task Phase_three_upgrade_preserves_pending_events_and_enforces_notification_scope_and_deduplication()
    {
        var configured = Environment.GetEnvironmentVariable("FLOWFORGE_TEST_CONNECTION")!;
        var database = "flowforge_phase4_upgrade_" + Guid.NewGuid().ToString("N");
        await using var admin = new NpgsqlConnection(configured);
        await admin.OpenAsync();
        await using (var create = new NpgsqlCommand($"CREATE DATABASE {database}", admin)) await create.ExecuteNonQueryAsync();
        try
        {
            var settings = new NpgsqlConnectionStringBuilder(configured) { Database = database };
            await using var db = new FlowForgeDbContext(new DbContextOptionsBuilder<FlowForgeDbContext>().UseNpgsql(settings.ConnectionString).Options);
            await db.GetService<IMigrator>().MigrateAsync(db.Database.GetMigrations().Single(x => x.EndsWith("_PhaseThreeCompletion")));
            var user = new User { Email = "upgrade@example.com", NormalizedEmail = "UPGRADE@EXAMPLE.COM", DisplayName = "Upgrade", PasswordHash = "test" };
            var workspace = new Workspace { Name = "Upgrade", CreatedByUserId = user.Id };
            var member = new WorkspaceMembership { WorkspaceId = workspace.Id, UserId = user.Id, Role = WorkspaceRole.Owner };
            var project = new Project { WorkspaceId = workspace.Id, ProjectKey = "UP", Name = "Upgrade" };
            var otherProject = new Project { WorkspaceId = workspace.Id, ProjectKey = "OTHER", Name = "Other" };
            var status = new ProjectStatus { ProjectId = project.Id, Name = "Todo", Category = "Open" };
            var item = new WorkItem { WorkspaceId = workspace.Id, ProjectId = project.Id, ItemNumber = 1, Title = "Existing", Type = "Task",
                StatusId = status.Id, Priority = "Medium", ReporterMembershipId = member.Id };
            var e = new DomainEvent { WorkspaceId = workspace.Id, ProjectId = project.Id, EntityId = item.Id,
                EventType = "WorkItemCreated", Payload = JsonSerializer.Serialize(new { item.AssigneeMembershipId }), CorrelationId = "upgrade" };
            db.Users.Add(user); db.Workspaces.Add(workspace); db.WorkspaceMemberships.Add(member);
            db.Projects.AddRange(project, otherProject); db.ProjectStatuses.Add(status); db.WorkItems.Add(item); db.DomainEvents.Add(e);
            await db.SaveChangesAsync();
            var outboxId = Guid.NewGuid();
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO outbox_messages (id, event_id, message_type, available_at, attempt_count)
                VALUES ({outboxId}, {e.Id}, 'DomainEvent', {DateTimeOffset.UtcNow}, 0)
                """);
            await db.Database.MigrateAsync();
            db.ChangeTracker.Clear();
            var outbox = await db.OutboxMessages.SingleAsync(x => x.Id == outboxId);
            Assert.Null(outbox.LockId); Assert.Null(outbox.FailedAt); Assert.Null(outbox.ProcessedAt);
            Assert.Equal(item.Id, (await db.WorkItems.SingleAsync()).Id);
            Assert.False(db.Database.HasPendingModelChanges());
            var n = new Notification { WorkspaceId = workspace.Id, ProjectId = project.Id, WorkItemId = item.Id,
                RecipientUserId = user.Id, Type = "Assigned", IdempotencyKey = "one-notification" };
            db.Notifications.Add(n);
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
            db.Notifications.Add(new Notification { WorkspaceId = workspace.Id, ProjectId = project.Id, WorkItemId = item.Id,
                RecipientUserId = user.Id, Type = "Assigned", IdempotencyKey = "one-notification" });
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            db.ChangeTracker.Clear();
            db.Notifications.Add(new Notification { WorkspaceId = workspace.Id, ProjectId = otherProject.Id, WorkItemId = item.Id,
                RecipientUserId = user.Id, Type = "Assigned", IdempotencyKey = "wrong-project" });
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            db.ChangeTracker.Clear();
            db.Notifications.Add(new Notification { WorkspaceId = workspace.Id, ProjectId = project.Id, WorkItemId = item.Id,
                RecipientUserId = user.Id, Type = "Unknown", IdempotencyKey = "invalid-type" });
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }
        finally
        {
            NpgsqlConnection.ClearAllPools();
            await using var drop = new NpgsqlCommand($"DROP DATABASE {database} WITH (FORCE)", admin);
            await drop.ExecuteNonQueryAsync();
        }
    }
}
