using FlowForge.Domain.Workflows;

namespace FlowForge.Api.Contracts.Workflows;

public sealed record WorkflowExecutionDto(Guid Id, Guid WorkflowId, Guid WorkflowVersionId, Guid EventId,
    Guid ProjectId, Guid WorkItemId, string Status, bool? ConditionsMatched, int AttemptCount,
    DateTimeOffset CreatedAt, DateTimeOffset? StartedAt, DateTimeOffset? FinishedAt, string? SafeError,
    IReadOnlyList<WorkflowActionResultDto> Actions, IReadOnlyList<WorkflowEmailDeliveryDto> EmailDeliveries,
    int Version, WorkflowDefinition Definition, string CorrelationId, Guid? ActorUserId, Guid? OriginExecutionId, int Depth);
