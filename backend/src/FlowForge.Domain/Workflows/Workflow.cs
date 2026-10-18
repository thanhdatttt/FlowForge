namespace FlowForge.Domain.Workflows;

public sealed class Workflow
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid WorkspaceId { get; set; }
    public Guid? ProjectId { get; set; }
    public required string Name { get; set; }
    public Guid CreatedByUserId { get; set; }
    public bool IsEnabled { get; set; }
    public DateTimeOffset? EnabledAt { get; set; }
    public int CurrentVersion { get; set; } = 1;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
