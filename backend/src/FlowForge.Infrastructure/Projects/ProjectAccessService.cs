using FlowForge.Domain.Projects;
using FlowForge.Domain.Workspaces;
using Microsoft.EntityFrameworkCore;

namespace FlowForge.Infrastructure.Projects;

public sealed class ProjectAccessService(FlowForgeDbContext db)
{
    public IQueryable<Project> VisibleProjects(Guid workspaceId, Guid userId, WorkspaceRole role)
    {
        var query = db.Projects.Where(x => x.WorkspaceId == workspaceId);
        if (ProjectPermissions.CanCreate(role)) return query;
        return query.Where(project => db.ProjectMemberships.Any(member => member.ProjectId == project.Id &&
            member.RemovedAt == null && member.WorkspaceMembership.UserId == userId &&
            member.WorkspaceMembership.RemovedAt == null && member.WorkspaceMembership.User.IsActive));
    }
}
