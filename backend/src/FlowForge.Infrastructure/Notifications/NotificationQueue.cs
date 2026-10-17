using FlowForge.Application.Email;
using FlowForge.Application.Notifications;
using FlowForge.Domain.Notifications;
using FlowForge.Domain.Workspaces;
using Microsoft.EntityFrameworkCore;

namespace FlowForge.Infrastructure.Notifications;

// Adds queue rows to the caller's business transaction; never sends external messages here.
public sealed class NotificationQueue(FlowForgeDbContext db, IEmailSender sender,
    IEmailContentProtector protector, TimeProvider clock)
{
    public const string NotificationEmailBody = "A new notification is available in FlowForge. " +
        "Sign in to view notifications you can currently access.";

    public void Invitation(WorkspaceInvitation invitation, string url)
    {
        if (!sender.IsConfigured) return;
        db.EmailDeliveries.Add(new EmailDelivery
        {
            WorkspaceId = invitation.WorkspaceId, InvitationId = invitation.Id,
            RecipientEmail = invitation.NormalizedEmail, Subject = "FlowForge workspace invitation",
            ProtectedBody = protector.Protect($"You have been invited to a FlowForge workspace as {invitation.Role}.\n\n" +
                $"Sign in with the invited email address and verify it before accepting:\n{url}\n\n" +
                $"This invitation expires at {invitation.ExpiresAt:u}."),
            IdempotencyKey = $"invitation:{invitation.Id:N}", CreatedAt = clock.GetUtcNow(), NextAttemptAt = clock.GetUtcNow()
        });
    }

    public async Task<Notification> AddAsync(Notification notification, CancellationToken ct)
    {
        var existing = await db.Notifications.SingleOrDefaultAsync(x => x.IdempotencyKey == notification.IdempotencyKey, ct);
        if (existing != null) return existing;
        notification.CreatedAt = clock.GetUtcNow();
        db.Notifications.Add(notification);
        if (sender.IsConfigured && notification.Type != "Invited")
        {
            var email = await db.Users.Where(x => x.Id == notification.RecipientUserId && x.IsActive).Select(x => x.Email).SingleAsync(ct);
            db.EmailDeliveries.Add(new EmailDelivery
            {
                WorkspaceId = notification.WorkspaceId, NotificationId = notification.Id, RecipientEmail = email,
                Subject = "FlowForge notification", ProtectedBody = protector.Protect(NotificationEmailBody),
                IdempotencyKey = $"notification:{notification.Id:N}", CreatedAt = clock.GetUtcNow(), NextAttemptAt = clock.GetUtcNow()
            });
        }
        return notification;
    }
}
