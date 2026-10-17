namespace FlowForge.Domain.WorkItems;

public sealed class WorkItemDependency
{
    public Guid ProjectId { get; set; }
    public Guid WorkItemId { get; set; }
    public Guid DependsOnItemId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
