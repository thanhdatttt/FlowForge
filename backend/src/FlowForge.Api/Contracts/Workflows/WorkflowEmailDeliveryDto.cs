namespace FlowForge.Api.Contracts.Workflows;

public sealed record WorkflowEmailDeliveryDto(Guid Id, Guid? RecipientUserId, string Status, int AttemptCount,
    DateTimeOffset? CompletedAt, string? SafeError);
