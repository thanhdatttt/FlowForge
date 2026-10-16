using FlowForge.Domain.Workspaces;

namespace FlowForge.Domain.Projects;

public sealed class Project
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid WorkspaceId { get; set; }
    public required string ProjectKey { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public Guid? LeadMembershipId { get; set; }
    public WorkspaceMembership? LeadMembership { get; set; }
    public DateOnly? StartsOn { get; set; }
    public DateOnly? TargetOn { get; set; }
    public DateTimeOffset? ArchivedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public long Version { get; set; } = 1;
}
