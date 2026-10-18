using FlowForge.Domain.Workflows;

namespace FlowForge.Api.Contracts.Workflows;

public sealed record WorkflowDto(Guid Id, Guid WorkspaceId, Guid? ProjectId, string Name, Guid CreatedByUserId,
    bool IsEnabled, int Version, WorkflowDefinition Definition, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
