namespace FlowForge.Domain.WorkItems;

// Persisted together with the business change and dispatched after commit.
public sealed class DomainEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid WorkspaceId { get; set; }
    public Guid? ProjectId { get; set; }
    public required string EventType { get; set; }
    public Guid EntityId { get; set; }
    public required string Payload { get; set; }
    public Guid? ActorUserId { get; set; }
    public required string CorrelationId { get; set; }
    public Guid? OriginExecutionId { get; set; }
    public int Depth { get; set; }
    public DateTimeOffset? WorkflowProcessedAt { get; set; }
    public DateOnly? TriggerDate { get; set; }
    public string? DeduplicationKey { get; set; }
    public DateTimeOffset OccurredAt { get; set; } = DateTimeOffset.UtcNow;
}
