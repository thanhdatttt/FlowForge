namespace FlowForge.Domain.Workflows;

public sealed record WorkflowCondition(string? Field, string? Operator, IReadOnlyList<string>? Values);
