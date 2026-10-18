using System.Text.Json;
using FlowForge.Application.Notifications;
using FlowForge.Domain.Notifications;
using FlowForge.Domain.WorkItems;
using FlowForge.Infrastructure.Workspaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FlowForge.Infrastructure.Notifications;

public sealed class NotificationDispatcher(FlowForgeDbContext db, NotificationQueue queue, NotificationAccess access,
    IRealtimePublisher realtime, TimeProvider clock, IOptions<NotificationJobOptions> options,
    ILogger<NotificationDispatcher> logger)
{
    public async Task<bool> DispatchNextAsync(CancellationToken ct)
    {
        db.ChangeTracker.Clear();
        var now = clock.GetUtcNow();
        var max = options.Value.MaxAttempts;
        await db.OutboxMessages.Where(x => x.ProcessedAt == null && x.FailedAt == null && x.AttemptCount >= max && x.AvailableAt <= now)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.FailedAt, now).SetProperty(x => x.LockId, (Guid?)null)
                .SetProperty(x => x.SafeError, "Delivery attempt limit reached."), ct);
        OutboxMessage? message;
        await using (var claim = await db.Database.BeginTransactionAsync(ct))
        {
            message = await db.OutboxMessages.FromSqlInterpolated($"""
                SELECT * FROM outbox_messages WHERE processed_at IS NULL AND failed_at IS NULL
                AND available_at <= {now} AND attempt_count < {max}
                ORDER BY available_at, id LIMIT 1 FOR UPDATE SKIP LOCKED
                """).SingleOrDefaultAsync(ct);
            if (message == null) return false;
            message.LockId = Guid.NewGuid();
            message.AttemptCount++;
            // A crashed process releases the claim after two minutes.
            message.AvailableAt = now.AddMinutes(2);
            await db.SaveChangesAsync(ct);
            await claim.CommitAsync(ct);
        }
        var messageId = message.Id;
        var lockId = message.LockId;
        try
        {
            var domainEvent = await db.DomainEvents.AsNoTracking().SingleAsync(x => x.Id == message.EventId, ct);
            List<Notification> notifications;
            await using (var transaction = await db.Database.BeginTransactionAsync(ct))
            {
                db.ChangeTracker.Clear();
                if (!await OwnsClaimAsync(messageId, lockId, ct)) return true;
                await WorkspaceLock.AcquireAsync(db, domainEvent.WorkspaceId, ct);
                notifications = await CreateNotificationsAsync(domainEvent, ct);
                await db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
            }
            // No client can observe a notification before it is committed. The workspace lock
            // serializes access revocation with authorization and sending to subscribed users.
            await using (var transaction = await db.Database.BeginTransactionAsync(ct))
            {
                db.ChangeTracker.Clear();
                if (!await OwnsClaimAsync(messageId, lockId, ct)) return true;
                await WorkspaceLock.AcquireAsync(db, domainEvent.WorkspaceId, ct);
                if (domainEvent.ProjectId is Guid projectId)
                {
                    var recipients = await access.ProjectRecipients(domainEvent.WorkspaceId, projectId).ToListAsync(ct);
                    await realtime.PublishProjectAsync(new(domainEvent.Id, domainEvent.EventType, domainEvent.WorkspaceId,
                        projectId, domainEvent.EntityId, domainEvent.OccurredAt), recipients, ct);
                }
                foreach (var notification in notifications)
                    if (await access.Visible(notification.RecipientUserId).AnyAsync(x => x.Id == notification.Id, ct))
                        await realtime.PublishNotificationAsync(notification.RecipientUserId, notification.Id, ct);
                await db.OutboxMessages.Where(x => x.Id == messageId && x.LockId == lockId)
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.ProcessedAt, clock.GetUtcNow())
                        .SetProperty(x => x.LockId, (Guid?)null).SetProperty(x => x.SafeError, (string?)null), ct);
                await transaction.CommitAsync(ct);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception error)
        {
            db.ChangeTracker.Clear();
            var terminal = !NotificationRetry.IsTransient(error) || message.AttemptCount >= max;
            await db.OutboxMessages.Where(x => x.Id == messageId && x.LockId == lockId && x.ProcessedAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.LockId, (Guid?)null)
                    .SetProperty(x => x.AvailableAt, clock.GetUtcNow().Add(NotificationRetry.Delay(message.AttemptCount)))
                    .SetProperty(x => x.FailedAt, terminal ? clock.GetUtcNow() : (DateTimeOffset?)null)
                    .SetProperty(x => x.SafeError, NotificationRetry.SafeError(error)), ct);
            logger.LogWarning("Outbox message {MessageId} attempt {Attempt} failed ({ErrorType}); terminal: {Terminal}.", messageId, message.AttemptCount, error.GetType().Name, terminal);
        }
        return true;
    }

    private async Task<bool> OwnsClaimAsync(Guid id, Guid? lockId, CancellationToken ct) =>
        await db.OutboxMessages.FromSqlInterpolated($"""
            SELECT * FROM outbox_messages WHERE id = {id} AND lock_id = {lockId}
            AND processed_at IS NULL AND failed_at IS NULL AND available_at > {clock.GetUtcNow()} FOR UPDATE
            """).SingleOrDefaultAsync(ct) != null;

    private async Task<List<Notification>> CreateNotificationsAsync(DomainEvent e, CancellationToken ct)
    {
        if (e.EventType == "WorkflowNotificationCreated")
        {
            var notification = await db.Notifications.SingleOrDefaultAsync(x => x.Id == e.EntityId &&
                x.WorkspaceId == e.WorkspaceId && x.ProjectId == e.ProjectId && x.Type == "Workflow", ct);
            return notification == null ? [] : [notification];
        }
        if (e.EventType == "InvitationCreated")
        {
            if (!await access.InvitationIsDeliverableAsync(e.EntityId, ct)) return [];
            var invitation = await db.WorkspaceInvitations.AsNoTracking().SingleAsync(x => x.Id == e.EntityId, ct);
            var userId = await db.Users.Where(x => x.IsActive && x.NormalizedEmail == invitation.NormalizedEmail)
                .Select(x => (Guid?)x.Id).SingleOrDefaultAsync(ct);
            if (userId == null || userId == e.ActorUserId) return [];
            return [await queue.AddAsync(new Notification { WorkspaceId = e.WorkspaceId,
                RecipientUserId = userId.Value, InvitationId = e.EntityId, Type = "Invited",
                IdempotencyKey = $"event:{e.Id:N}:{userId:N}" }, ct)];
        }
        if (e.ProjectId is not Guid projectId) return [];
        if (e.EventType is not ("WorkItemCreated" or "WorkItemAssignmentChanged" or "CommentAdded" or "CommentUpdated" or "WorkItemOverdue"))
            return [];
        using var json = JsonDocument.Parse(e.Payload);
        var payload = json.RootElement;
        var commentEvent = e.EventType is "CommentAdded" or "CommentUpdated";
        var itemId = commentEvent ? payload.GetProperty("WorkItemId").GetGuid() : e.EntityId;
        var item = await db.WorkItems.AsNoTracking().SingleOrDefaultAsync(x => x.Id == itemId && x.ProjectId == projectId &&
            x.ArchivedAt == null && db.Projects.Any(p => p.Id == projectId && p.ArchivedAt == null), ct);
        if (item == null) return [];
        var recipients = new List<Guid>();
        var type = commentEvent ? "Mentioned" : e.EventType == "WorkItemOverdue" ? "Overdue" : "Assigned";
        if (commentEvent)
        {
            var field = e.EventType == "CommentUpdated" ? "NewMentionedUserIds" : "MentionedUserIds";
            var mentioned = payload.GetProperty(field).EnumerateArray().Select(x => x.GetGuid()).Distinct().ToArray();
            recipients = await db.CommentMentions.Where(x => x.CommentId == e.EntityId && mentioned.Contains(x.MentionedUserId) &&
                db.WorkComments.Any(c => c.Id == e.EntityId && c.WorkItemId == item.Id && c.DeletedAt == null))
                .Select(x => x.MentionedUserId).ToListAsync(ct);
        }
        else
        {
            var field = e.EventType == "WorkItemAssignmentChanged" ? "NewAssigneeMembershipId" : "AssigneeMembershipId";
            var assigneeId = payload.GetProperty(field).ValueKind == JsonValueKind.Null ? (Guid?)null : payload.GetProperty(field).GetGuid();
            if (assigneeId != null && item.AssigneeMembershipId == assigneeId)
            {
                if (type == "Overdue" && (item.DueDate != payload.GetProperty("DueDate").Deserialize<DateOnly>() ||
                    item.DueDate >= DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime) ||
                    await db.ProjectStatuses.AnyAsync(x => x.Id == item.StatusId && x.Category == "Done", ct))) return [];
                recipients = await db.WorkspaceMemberships.Where(x => x.Id == assigneeId && x.RemovedAt == null)
                    .Select(x => x.UserId).ToListAsync(ct);
            }
        }
        var notifications = new List<Notification>();
        foreach (var recipient in recipients.Where(x => x != e.ActorUserId))
        {
            if (!await access.ProjectRecipients(e.WorkspaceId, projectId).ContainsAsync(recipient, ct)) continue;
            notifications.Add(await queue.AddAsync(new Notification { WorkspaceId = e.WorkspaceId, ProjectId = projectId,
                WorkItemId = item.Id, CommentId = commentEvent ? e.EntityId : null, RecipientUserId = recipient, Type = type,
                DueDate = type == "Overdue" ? item.DueDate : null,
                IdempotencyKey = type == "Overdue" ? OverdueKey(item.Id, item.DueDate!.Value, recipient) : $"event:{e.Id:N}:{recipient:N}" }, ct));
        }
        return notifications;
    }

    internal static string OverdueKey(Guid itemId, DateOnly due, Guid recipient) => $"overdue:{itemId:N}:{due:yyyy-MM-dd}:{recipient:N}";
}
