namespace FlowForge.Api.Contracts.Workflows;

public sealed record WorkflowActionResultDto(Guid ActionId, string Status, int AttemptCount,
    DateTimeOffset? FinishedAt, string? SafeError);
