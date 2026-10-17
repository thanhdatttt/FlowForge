using FlowForge.Domain.Notifications;
using FlowForge.Domain.Workspaces;
using Microsoft.EntityFrameworkCore;

namespace FlowForge.Infrastructure.Notifications;

public sealed class NotificationAccess(FlowForgeDbContext db, TimeProvider clock)
{
    public IQueryable<Guid> ProjectRecipients(Guid workspaceId, Guid projectId) =>
        db.WorkspaceMemberships.Where(m => m.WorkspaceId == workspaceId && m.RemovedAt == null && m.User.IsActive &&
            db.Projects.Any(p => p.Id == projectId && p.WorkspaceId == workspaceId) &&
            (m.Role == WorkspaceRole.Owner || m.Role == WorkspaceRole.Admin || db.ProjectMemberships.Any(p =>
                p.WorkspaceId == workspaceId && p.ProjectId == projectId && p.WorkspaceMembershipId == m.Id && p.RemovedAt == null)))
            .Select(m => m.UserId);

    public IQueryable<Notification> Visible(Guid userId)
    {
        var now = clock.GetUtcNow();
        return db.Notifications.Where(n => n.RecipientUserId == userId && db.Users.Any(u => u.Id == userId && u.IsActive) &&
            (n.ProjectId != null
                ? db.WorkspaceMemberships.Any(m => m.WorkspaceId == n.WorkspaceId && m.UserId == userId && m.RemovedAt == null &&
                    (m.Role == WorkspaceRole.Owner || m.Role == WorkspaceRole.Admin || db.ProjectMemberships.Any(p =>
                        p.ProjectId == n.ProjectId && p.WorkspaceMembershipId == m.Id && p.RemovedAt == null)))
                : db.WorkspaceInvitations.Any(i => i.Id == n.InvitationId && i.WorkspaceId == n.WorkspaceId &&
                    i.AcceptedAt == null && i.RevokedAt == null && i.ExpiresAt > now &&
                    db.Users.Any(u => u.Id == userId && u.NormalizedEmail == i.NormalizedEmail) &&
                    db.WorkspaceMemberships.Any(m => m.WorkspaceId == i.WorkspaceId && m.UserId == i.InvitedByUserId &&
                        m.RemovedAt == null && m.User.IsActive && (m.Role == WorkspaceRole.Owner ||
                            m.Role == WorkspaceRole.Admin && i.Role != WorkspaceRole.Admin)))));
    }

    public Task<bool> InvitationIsDeliverableAsync(Guid invitationId, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        return db.WorkspaceInvitations.AnyAsync(i => i.Id == invitationId && i.AcceptedAt == null && i.RevokedAt == null &&
            i.ExpiresAt > now && !db.WorkspaceMemberships.Any(m => m.WorkspaceId == i.WorkspaceId && m.RemovedAt == null &&
                m.User.NormalizedEmail == i.NormalizedEmail) &&
            db.WorkspaceMemberships.Any(m => m.WorkspaceId == i.WorkspaceId && m.UserId == i.InvitedByUserId &&
                m.RemovedAt == null && m.User.IsActive && (m.Role == WorkspaceRole.Owner ||
                    m.Role == WorkspaceRole.Admin && i.Role != WorkspaceRole.Admin)), ct);
    }
}
