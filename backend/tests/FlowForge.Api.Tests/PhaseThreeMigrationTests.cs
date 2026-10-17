using FlowForge.Domain.Identity;
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

public sealed class PhaseThreeMigrationTests
{
    [PostgresFact]
    public async Task Initial_phase_three_upgrade_seeds_statuses_for_existing_projects()
    {
        var configured = Environment.GetEnvironmentVariable("FLOWFORGE_TEST_CONNECTION")!;
        var database = "flowforge_phase3_seed_" + Guid.NewGuid().ToString("N");
        await using var admin = new NpgsqlConnection(configured);
        await admin.OpenAsync();
        await using (var create = new NpgsqlCommand($"CREATE DATABASE {database}", admin))
            await create.ExecuteNonQueryAsync();
        try
        {
            var settings = new NpgsqlConnectionStringBuilder(configured) { Database = database };
            await using var db = new FlowForgeDbContext(new DbContextOptionsBuilder<FlowForgeDbContext>()
                .UseNpgsql(settings.ConnectionString).Options);
            var previousMigration = db.Database.GetMigrations().Single(x => x.EndsWith("_EmailVerificationOtp"));
            await db.GetService<IMigrator>().MigrateAsync(previousMigration);
            var user = new User { Email = "seed@example.com", NormalizedEmail = "SEED@EXAMPLE.COM",
                DisplayName = "Seed", PasswordHash = "test-only" };
            var workspace = new Workspace { Name = "Seed", CreatedByUserId = user.Id };
            db.Users.Add(user);
            db.Workspaces.Add(workspace);
            db.WorkspaceMemberships.Add(new WorkspaceMembership { WorkspaceId = workspace.Id,
                UserId = user.Id, Role = WorkspaceRole.Owner });
            await db.SaveChangesAsync();
            var projectId = Guid.NewGuid();
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO projects (id, workspace_id, project_key, name, created_at, updated_at, version)
                VALUES ({projectId}, {workspace.Id}, 'OLD', 'Existing project',
                    {DateTimeOffset.UtcNow}, {DateTimeOffset.UtcNow}, {1L})
                """);
            await db.Database.MigrateAsync();
            db.ChangeTracker.Clear();
            Assert.Equal(1, (await db.Projects.SingleAsync(x => x.Id == projectId)).NextItemNumber);
            var names = await db.ProjectStatuses.Where(x => x.ProjectId == projectId)
                .OrderBy(x => x.SortOrder).Select(x => x.Name).ToListAsync();
            Assert.Equal(new[] { "Todo", "InProgress", "InReview", "Done", "Blocked" }, names);
        }
        finally
        {
            await using var drop = new NpgsqlCommand($"DROP DATABASE {database} WITH (FORCE)", admin);
            await drop.ExecuteNonQueryAsync();
        }
    }

    [PostgresFact]
    public async Task Completion_upgrade_backfills_existing_work_item_activity()
    {
        var configured = Environment.GetEnvironmentVariable("FLOWFORGE_TEST_CONNECTION")!;
        var database = "flowforge_phase3_upgrade_" + Guid.NewGuid().ToString("N");
        await using var admin = new NpgsqlConnection(configured);
        await admin.OpenAsync();
        await using (var create = new NpgsqlCommand($"CREATE DATABASE {database}", admin))
            await create.ExecuteNonQueryAsync();
        try
        {
            var settings = new NpgsqlConnectionStringBuilder(configured) { Database = database };
            await using var db = new FlowForgeDbContext(new DbContextOptionsBuilder<FlowForgeDbContext>()
                .UseNpgsql(settings.ConnectionString).Options);
            var previousMigration = db.Database.GetMigrations().Single(x => x.EndsWith("_PhaseThreeWorkItems"));
            await db.GetService<IMigrator>().MigrateAsync(previousMigration);
            var user = new User { Email = "phase3@example.com", NormalizedEmail = "PHASE3@EXAMPLE.COM",
                DisplayName = "Phase 3", PasswordHash = "test-only" };
            var workspace = new Workspace { Name = "Upgrade", CreatedByUserId = user.Id };
            var membership = new WorkspaceMembership { WorkspaceId = workspace.Id, UserId = user.Id, Role = WorkspaceRole.Owner };
            var project = new Project { WorkspaceId = workspace.Id, Name = "Upgrade", ProjectKey = "UP" };
            var status = new ProjectStatus { ProjectId = project.Id, Name = "Todo", Category = "Open", SortOrder = 0 };
            var item = new WorkItem { WorkspaceId = workspace.Id, ProjectId = project.Id, ItemNumber = 1,
                Title = "Existing", Type = "Task", StatusId = status.Id, Priority = "Medium",
                ReporterMembershipId = membership.Id };
            db.Users.Add(user);
            db.Workspaces.Add(workspace);
            db.WorkspaceMemberships.Add(membership);
            db.Projects.Add(project);
            db.ProjectStatuses.Add(status);
            db.WorkItems.Add(item);
            await db.SaveChangesAsync();
            var activityId = Guid.NewGuid();
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO activity_records (id, workspace_id, project_id, actor_user_id, actor_kind,
                    action, entity_type, entity_id, occurred_at)
                VALUES ({activityId}, {workspace.Id}, {project.Id}, {user.Id}, 'User',
                    'work-item.created', 'WorkItem', {item.Id}, {DateTimeOffset.UtcNow})
                """);
            await db.Database.MigrateAsync();
            db.ChangeTracker.Clear();
            Assert.Equal(item.Id, (await db.ActivityRecords.SingleAsync(x => x.Id == activityId)).WorkItemId);
            Assert.Empty(await db.DomainEvents.ToListAsync());
            Assert.Empty(await db.OutboxMessages.ToListAsync());
        }
        finally
        {
            await using var drop = new NpgsqlCommand($"DROP DATABASE {database} WITH (FORCE)", admin);
            await drop.ExecuteNonQueryAsync();
        }
    }
}
