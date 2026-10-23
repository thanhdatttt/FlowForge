namespace FlowForge.Api.Contracts.Ai;

public sealed record AiAcceptedTasksDto(Guid BatchId, IReadOnlyList<AiCreatedTaskDto> Tasks);
