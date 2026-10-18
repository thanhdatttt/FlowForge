using FlowForge.Domain.Workflows;

namespace FlowForge.Api.Contracts.Workflows;

public sealed record WorkflowWriteDto(string? Name, Guid? ProjectId, WorkflowDefinition? Definition, int? Version = null);
