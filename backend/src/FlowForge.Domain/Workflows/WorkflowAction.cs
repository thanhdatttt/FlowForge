namespace FlowForge.Domain.Workflows;

public sealed record WorkflowAction(Guid Id, string? Type, Guid? UserId = null, string? Value = null);
