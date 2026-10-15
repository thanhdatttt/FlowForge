using FlowForge.Domain.Workspaces;
using Microsoft.EntityFrameworkCore;

namespace FlowForge.Infrastructure.Workspaces;

public sealed class WorkspaceAccessService(FlowForgeDbContext db)
{
    public Task<WorkspaceRole?> GetRoleAsync(Guid workspaceId, Guid userId, CancellationToken cancellationToken) =>
        db.WorkspaceMemberships.AsNoTracking()
            .Where(membership => membership.WorkspaceId == workspaceId && membership.UserId == userId &&
                membership.RemovedAt == null && membership.User.IsActive)
            .Select(membership => (WorkspaceRole?)membership.Role)
            .SingleOrDefaultAsync(cancellationToken);
}
