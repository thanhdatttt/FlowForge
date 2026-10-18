using FlowForge.Application.Email;
using FlowForge.Application.Notifications;
using FlowForge.Domain.Notifications;
using FlowForge.Infrastructure.Workflows;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FlowForge.Infrastructure.Notifications;

public sealed class EmailDeliveryProcessor(FlowForgeDbContext db, NotificationAccess access, IEmailSender sender,
    IEmailContentProtector protector, TimeProvider clock, WorkflowAccess workflows, IOptions<NotificationJobOptions> options,
    ILogger<EmailDeliveryProcessor> logger)
{
    public async Task<bool> SendNextAsync(CancellationToken ct)
    {
        if (!sender.IsConfigured) return false;
        db.ChangeTracker.Clear();
        var now = clock.GetUtcNow();
        var max = options.Value.MaxAttempts;
        await db.EmailDeliveries.Where(x => x.Status == "Pending" && x.AttemptCount >= max && x.NextAttemptAt <= now)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, "Failed").SetProperty(x => x.CompletedAt, now)
                .SetProperty(x => x.LockId, (Guid?)null).SetProperty(x => x.SafeError, "Delivery attempt limit reached.")
                .SetProperty(x => x.ProtectedBody, ""), ct);
        EmailDelivery? delivery;
        await using (var claim = await db.Database.BeginTransactionAsync(ct))
        {
            delivery = await db.EmailDeliveries.FromSqlInterpolated($"""
                SELECT * FROM email_deliveries WHERE status = 'Pending' AND next_attempt_at <= {now} AND attempt_count < {max}
                ORDER BY next_attempt_at, id LIMIT 1 FOR UPDATE SKIP LOCKED
                """).SingleOrDefaultAsync(ct);
            if (delivery == null) return false;
            delivery.LockId = Guid.NewGuid();
            delivery.AttemptCount++;
            delivery.NextAttemptAt = now.AddMinutes(2);
            await db.SaveChangesAsync(ct);
            await claim.CommitAsync(ct);
        }
        var deliveryId = delivery.Id;
        var lockId = delivery.LockId;
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            db.ChangeTracker.Clear();
            var owned = await db.EmailDeliveries.FromSqlInterpolated($"""
                SELECT * FROM email_deliveries WHERE id = {deliveryId} AND lock_id = {lockId}
                AND status = 'Pending' AND next_attempt_at > {clock.GetUtcNow()} FOR UPDATE
                """).SingleOrDefaultAsync(ct);
            if (owned == null) return true;
            delivery = owned;
            // Recheck immediately before SMTP, without holding a workspace lock across network I/O.
            // Notification email contains no private resource data if access changes during the send.
            // Invitation tokens remain subject to the existing acceptance/revocation checks.
            var eligible = delivery.InvitationId is Guid invitationId
                ? await access.InvitationIsDeliverableAsync(invitationId, ct)
                : delivery.WorkflowExecutionId != null ? await WorkflowIsDeliverableAsync(delivery, ct)
                : await NotificationIsDeliverableAsync(delivery, ct);
            if (eligible)
                await sender.SendAsync(delivery.RecipientEmail, delivery.Subject,
                    delivery.InvitationId != null ? protector.Unprotect(delivery.ProtectedBody) : NotificationQueue.NotificationEmailBody, ct);
            await db.EmailDeliveries.Where(x => x.Id == deliveryId && x.LockId == lockId)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, eligible ? "Sent" : "Cancelled")
                    .SetProperty(x => x.CompletedAt, clock.GetUtcNow()).SetProperty(x => x.LockId, (Guid?)null)
                    .SetProperty(x => x.SafeError, (string?)null).SetProperty(x => x.ProtectedBody, ""), ct);
            await transaction.CommitAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception error)
        {
            db.ChangeTracker.Clear();
            var terminal = !NotificationRetry.IsTransient(error) || delivery.AttemptCount >= max;
            await db.EmailDeliveries.Where(x => x.Id == deliveryId && x.LockId == lockId && x.Status == "Pending")
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.LockId, (Guid?)null)
                    .SetProperty(x => x.NextAttemptAt, clock.GetUtcNow().Add(NotificationRetry.Delay(delivery.AttemptCount)))
                    .SetProperty(x => x.Status, terminal ? "Failed" : "Pending")
                    .SetProperty(x => x.CompletedAt, terminal ? clock.GetUtcNow() : (DateTimeOffset?)null)
                    .SetProperty(x => x.ProtectedBody, terminal ? "" : delivery.ProtectedBody)
                    .SetProperty(x => x.SafeError, NotificationRetry.SafeError(error)), ct);
            logger.LogWarning("Email delivery {DeliveryId} attempt {Attempt} failed; terminal: {Terminal}.", deliveryId, delivery.AttemptCount, terminal);
        }
        return true;
    }

    private async Task<bool> WorkflowIsDeliverableAsync(EmailDelivery delivery, CancellationToken ct)
    {
        var execution = await db.WorkflowExecutions.AsNoTracking().SingleOrDefaultAsync(x => x.Id == delivery.WorkflowExecutionId, ct);
        if (execution == null || delivery.RecipientUserId is not Guid recipient) return false;
        var creator = await db.Workflows.Where(x => x.Id == execution.WorkflowId).Select(x => x.CreatedByUserId).SingleAsync(ct);
        return await workflows.CanExecuteAsync(delivery.WorkspaceId, execution.ProjectId, creator, ct) &&
            await access.ProjectRecipients(delivery.WorkspaceId, execution.ProjectId).ContainsAsync(recipient, ct) &&
            await db.Users.AnyAsync(x => x.Id == recipient && x.Email == delivery.RecipientEmail && x.IsActive, ct) &&
            await db.WorkItems.AnyAsync(x => x.Id == execution.WorkItemId && x.ArchivedAt == null, ct);
    }

    private async Task<bool> NotificationIsDeliverableAsync(EmailDelivery delivery, CancellationToken ct)
    {
        var notification = await db.Notifications.AsNoTracking().SingleOrDefaultAsync(x => x.Id == delivery.NotificationId, ct);
        if (notification == null || !await access.Visible(notification.RecipientUserId).AnyAsync(x => x.Id == notification.Id, ct)) return false;
        if (!await db.Users.AnyAsync(x => x.Id == notification.RecipientUserId && x.Email == delivery.RecipientEmail && x.IsActive, ct)) return false;
        var item = await db.WorkItems.AsNoTracking().SingleOrDefaultAsync(x => x.Id == notification.WorkItemId && x.ArchivedAt == null &&
            db.Projects.Any(p => p.Id == x.ProjectId && p.ArchivedAt == null), ct);
        if (item == null) return false;
        if (notification.Type == "Workflow") return true;
        if (notification.Type == "Mentioned")
            return await db.CommentMentions.AnyAsync(x => x.CommentId == notification.CommentId && x.MentionedUserId == notification.RecipientUserId &&
                db.WorkComments.Any(c => c.Id == x.CommentId && c.DeletedAt == null), ct);
        if (!await db.WorkspaceMemberships.AnyAsync(x => x.Id == item.AssigneeMembershipId && x.UserId == notification.RecipientUserId && x.RemovedAt == null, ct)) return false;
        return notification.Type != "Overdue" || item.DueDate == notification.DueDate &&
            item.DueDate < DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime) &&
            !await db.ProjectStatuses.AnyAsync(x => x.Id == item.StatusId && x.Category == "Done", ct);
    }
}
