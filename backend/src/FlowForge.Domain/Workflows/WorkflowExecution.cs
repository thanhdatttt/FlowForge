namespace FlowForge.Domain.Workflows;

public sealed class WorkflowExecution
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid WorkspaceId { get; set; }
    public Guid ProjectId { get; set; }
    public Guid WorkItemId { get; set; }
    public Guid WorkflowId { get; set; }
    public Guid WorkflowVersionId { get; set; }
    public Guid EventId { get; set; }
    public string Status { get; set; } = "Pending";
    public bool? ConditionsMatched { get; set; }
    public int AttemptCount { get; set; }
    public Guid? LockId { get; set; }
    public DateTimeOffset NextAttemptAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
    public string? SafeError { get; set; }
}
