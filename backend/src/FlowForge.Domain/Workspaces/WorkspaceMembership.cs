using FlowForge.Domain.Identity;

namespace FlowForge.Domain.Workspaces;

public sealed class WorkspaceMembership
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid WorkspaceId { get; set; }
    public Guid UserId { get; set; }
    public WorkspaceRole Role { get; set; }
    public DateTimeOffset JoinedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? RemovedAt { get; set; }
    public Workspace Workspace { get; set; } = null!;
    public User User { get; set; } = null!;
}
