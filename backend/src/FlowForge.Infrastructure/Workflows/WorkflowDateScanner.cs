using System.Globalization;
using System.Text.Json;
using FlowForge.Domain.WorkItems;
using FlowForge.Infrastructure.Workspaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FlowForge.Infrastructure.Workflows;

// Calendar triggers are independent of assignee-specific notification reminders.
public sealed class WorkflowDateScanner(FlowForgeDbContext db, TimeProvider clock, IOptions<WorkflowJobOptions> options)
{
    public async Task<int> ScanAsync(CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var created = 0;
        foreach (var type in new[] { "WorkItemDueDateReached", "WorkItemDueDateOverdue" })
        {
            db.ChangeTracker.Clear();
            var candidates = await db.WorkItems.AsNoTracking().Where(x => x.ArchivedAt == null && x.DueDate != null &&
                (type == "WorkItemDueDateReached" ? x.DueDate == today : x.DueDate < today) &&
                db.ProjectStatuses.Any(s => s.Id == x.StatusId && s.Category != "Done") &&
                db.Projects.Any(p => p.Id == x.ProjectId && p.ArchivedAt == null) &&
                db.Workflows.Any(w => w.WorkspaceId == x.WorkspaceId && (w.ProjectId == null || w.ProjectId == x.ProjectId) &&
                    w.IsEnabled && db.WorkflowVersions.Any(v => v.WorkflowId == w.Id && v.VersionNumber == w.CurrentVersion && v.TriggerType == type)) &&
                !db.DomainEvents.Any(e => e.EventType == type && e.EntityId == x.Id && e.DeduplicationKey != null &&
                    e.TriggerDate == x.DueDate))
                .OrderBy(x => x.DueDate).ThenBy(x => x.Id).Take(options.Value.BatchSize).ToListAsync(ct);
            foreach (var candidate in candidates)
            {
                db.ChangeTracker.Clear();
                await using var transaction = await db.Database.BeginTransactionAsync(ct);
                await WorkspaceLock.AcquireAsync(db, candidate.WorkspaceId, ct);
                var item = await db.WorkItems.AsNoTracking().SingleAsync(x => x.Id == candidate.Id, ct);
                if (item.ArchivedAt != null || item.DueDate != candidate.DueDate ||
                    await db.Projects.AnyAsync(x => x.Id == item.ProjectId && x.ArchivedAt != null, ct) ||
                    await db.ProjectStatuses.AnyAsync(x => x.Id == item.StatusId && x.Category == "Done", ct)) continue;
                var key = type + ":" + item.Id + ":" + item.DueDate!.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                if (await db.DomainEvents.AnyAsync(x => x.DeduplicationKey == key, ct)) continue;
                var e = new DomainEvent { WorkspaceId = item.WorkspaceId, ProjectId = item.ProjectId, EntityId = item.Id,
                    EventType = type, CorrelationId = Guid.NewGuid().ToString("N"), OccurredAt = clock.GetUtcNow(),
                    DeduplicationKey = key, TriggerDate = item.DueDate, Payload = JsonSerializer.Serialize(new { item.DueDate }) };
                db.DomainEvents.Add(e);
                db.OutboxMessages.Add(new OutboxMessage { EventId = e.Id, MessageType = "DomainEvent", AvailableAt = clock.GetUtcNow() });
                await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct); created++;
            }
        }
        return created;
    }
}
