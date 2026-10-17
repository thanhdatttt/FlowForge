namespace FlowForge.Domain.WorkItems;

public sealed class ActivityRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid WorkspaceId { get; set; }
    public Guid? ProjectId { get; set; }
    public Guid? WorkItemId { get; set; }
    public Guid? ActorUserId { get; set; }
    public string ActorKind { get; set; } = "User";
    public required string Action { get; set; }
    public required string EntityType { get; set; }
    public Guid EntityId { get; set; }
    public string? BeforeValues { get; set; }
    public string? AfterValues { get; set; }
    public string? CorrelationId { get; set; }
    public DateTimeOffset OccurredAt { get; set; } = DateTimeOffset.UtcNow;
}
