using System.Text.Json;
using FlowForge.Domain.Notifications;
using FlowForge.Domain.WorkItems;
using FlowForge.Infrastructure.Workspaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FlowForge.Infrastructure.Notifications;

public sealed class OverdueChecker(FlowForgeDbContext db, NotificationAccess access, NotificationQueue queue,
    TimeProvider clock, IOptions<NotificationJobOptions> options)
{
    public async Task<int> CheckAsync(CancellationToken ct)
    {
        db.ChangeTracker.Clear();
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var candidates = await (from item in db.WorkItems
            join assignee in db.WorkspaceMemberships on item.AssigneeMembershipId equals assignee.Id
            where item.ArchivedAt == null && item.DueDate < today && assignee.RemovedAt == null && assignee.User.IsActive &&
                db.Projects.Any(p => p.Id == item.ProjectId && p.ArchivedAt == null) &&
                db.ProjectStatuses.Any(s => s.Id == item.StatusId && s.Category != "Done") &&
                (assignee.Role == Domain.Workspaces.WorkspaceRole.Owner || assignee.Role == Domain.Workspaces.WorkspaceRole.Admin ||
                    db.ProjectMemberships.Any(m => m.ProjectId == item.ProjectId && m.WorkspaceMembershipId == assignee.Id && m.RemovedAt == null)) &&
                !db.Notifications.Any(n => n.Type == "Overdue" && n.WorkItemId == item.Id && n.DueDate == item.DueDate && n.RecipientUserId == assignee.UserId)
            orderby item.DueDate, item.Id
            select new { item.Id, item.WorkspaceId }).TagWith("Overdue candidates").Take(options.Value.BatchSize).ToListAsync(ct);
        var created = 0;
        foreach (var candidate in candidates)
        {
            db.ChangeTracker.Clear();
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            await WorkspaceLock.AcquireAsync(db, candidate.WorkspaceId, ct);
            var item = await db.WorkItems.AsNoTracking().SingleAsync(x => x.Id == candidate.Id, ct);
            if (item.ArchivedAt != null || item.DueDate == null || item.DueDate >= today || item.AssigneeMembershipId == null ||
                await db.Projects.AnyAsync(x => x.Id == item.ProjectId && x.ArchivedAt != null, ct) ||
                await db.ProjectStatuses.AnyAsync(x => x.Id == item.StatusId && x.Category == "Done", ct)) continue;
            var recipient = await db.WorkspaceMemberships.Where(x => x.Id == item.AssigneeMembershipId && x.RemovedAt == null)
                .Select(x => (Guid?)x.UserId).SingleOrDefaultAsync(ct);
            if (recipient == null || !await access.ProjectRecipients(item.WorkspaceId, item.ProjectId).ContainsAsync(recipient.Value, ct)) continue;
            var key = NotificationDispatcher.OverdueKey(item.Id, item.DueDate.Value, recipient.Value);
            if (await db.Notifications.AnyAsync(x => x.IdempotencyKey == key, ct)) continue;
            await queue.AddAsync(new Notification { WorkspaceId = item.WorkspaceId, ProjectId = item.ProjectId, WorkItemId = item.Id,
                RecipientUserId = recipient.Value, Type = "Overdue", DueDate = item.DueDate, IdempotencyKey = key }, ct);
            var e = new DomainEvent { WorkspaceId = item.WorkspaceId, ProjectId = item.ProjectId, EntityId = item.Id,
                EventType = "WorkItemOverdue", CorrelationId = Guid.NewGuid().ToString("N"), OccurredAt = clock.GetUtcNow(),
                Payload = JsonSerializer.Serialize(new { item.AssigneeMembershipId, item.DueDate }) };
            db.DomainEvents.Add(e);
            db.OutboxMessages.Add(new OutboxMessage { EventId = e.Id, MessageType = "DomainEvent", AvailableAt = clock.GetUtcNow() });
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            created++;
        }
        return created;
    }
}
