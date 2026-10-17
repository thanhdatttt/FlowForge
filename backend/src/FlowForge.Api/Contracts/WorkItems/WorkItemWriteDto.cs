namespace FlowForge.Api.Contracts.WorkItems;

public sealed record WorkItemWriteDto(string? Title, string? Description, string? Type, string? Status,
    string? Priority, Guid? AssigneeUserId, Guid? ParentId, DateOnly? StartDate, DateOnly? DueDate,
    int? EstimateMinutes, IReadOnlyList<Guid>? LabelIds, long? Version);
