namespace FlowForge.Domain.Workflows;

public sealed record WorkflowDefinition(string? Trigger, IReadOnlyList<WorkflowCondition>? Conditions,
    IReadOnlyList<WorkflowAction>? Actions);
