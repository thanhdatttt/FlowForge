using FlowForge.Domain.Projects;
using FlowForge.Domain.Workspaces;
using Microsoft.EntityFrameworkCore;

namespace FlowForge.Infrastructure.Workflows;

public sealed class WorkflowAccess(FlowForgeDbContext db)
{
    public async Task<bool> CanManageAsync(Guid workspaceId, Guid? projectId, Guid userId, CancellationToken ct)
    {
        var member = await db.WorkspaceMemberships.AsNoTracking().SingleOrDefaultAsync(x => x.WorkspaceId == workspaceId &&
            x.UserId == userId && x.RemovedAt == null && x.User.IsActive, ct);
        if (member == null) return false;
        if (projectId == null) return member.Role is WorkspaceRole.Owner or WorkspaceRole.Admin;
        if (!await db.Projects.AnyAsync(x => x.Id == projectId && x.WorkspaceId == workspaceId, ct)) return false;
        return ProjectPermissions.CanManage(member.Role, await db.ProjectMemberships.AnyAsync(x => x.ProjectId == projectId &&
            x.WorkspaceMembershipId == member.Id && x.RemovedAt == null, ct));
    }

    public async Task<bool> CanExecuteAsync(Guid workspaceId, Guid projectId, Guid userId, CancellationToken ct)
    {
        var member = await db.WorkspaceMemberships.AsNoTracking().SingleOrDefaultAsync(x => x.WorkspaceId == workspaceId &&
            x.UserId == userId && x.RemovedAt == null && x.User.IsActive, ct);
        if (member == null) return false;
        var project = await db.Projects.AsNoTracking().SingleOrDefaultAsync(x => x.Id == projectId && x.WorkspaceId == workspaceId, ct);
        return project != null && ProjectPermissions.CanWriteResources(member.Role,
            await db.ProjectMemberships.AnyAsync(x => x.ProjectId == projectId && x.WorkspaceMembershipId == member.Id && x.RemovedAt == null, ct),
            project.ArchivedAt != null);
    }
}
