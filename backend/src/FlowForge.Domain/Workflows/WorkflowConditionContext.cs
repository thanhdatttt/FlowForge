namespace FlowForge.Domain.Workflows;

public sealed record WorkflowConditionContext(string Priority, string Status, string Type, Guid? Assignee,
    Guid? Actor, IReadOnlyList<string> Labels, DateOnly? DueDate, string Title, string? Description);
