using FlowForge.Domain.Workspaces;

namespace FlowForge.Domain.Projects;

public sealed class ProjectMembership
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid WorkspaceId { get; set; }
    public Guid ProjectId { get; set; }
    public Guid WorkspaceMembershipId { get; set; }
    public DateTimeOffset JoinedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? RemovedAt { get; set; }
    public Project Project { get; set; } = null!;
    public WorkspaceMembership WorkspaceMembership { get; set; } = null!;
}
