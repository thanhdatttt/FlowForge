namespace FlowForge.Domain.Workflows;

public sealed class WorkflowVersion
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid WorkflowId { get; set; }
    public int VersionNumber { get; set; }
    public required string TriggerType { get; set; }
    public required string Configuration { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
