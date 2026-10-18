namespace FlowForge.Domain.Workflows;

public sealed class WorkflowActionResult
{
    public Guid ExecutionId { get; set; }
    public Guid ActionId { get; set; }
    public string Status { get; set; } = "Pending";
    public int AttemptCount { get; set; }
    public string? SafeError { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
}
