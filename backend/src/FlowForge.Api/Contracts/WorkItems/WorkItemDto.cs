namespace FlowForge.Api.Contracts.WorkItems;

public sealed record WorkItemDto(Guid Id, Guid ProjectId, string Key, long ItemNumber, string Title,
    string? Description, string Type, string Status, string Priority, Guid ReporterUserId,
    Guid? AssigneeUserId, Guid? ParentId, DateOnly? StartDate, DateOnly? DueDate,
    int? EstimateMinutes, IReadOnlyList<Guid> LabelIds, IReadOnlyList<Guid> DependsOnItemIds,
    bool IsBlocked, bool NeedsReassignment, DateTimeOffset? ArchivedAt,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, long Version);
