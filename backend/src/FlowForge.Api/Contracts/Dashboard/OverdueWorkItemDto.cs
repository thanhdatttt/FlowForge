namespace FlowForge.Api.Contracts.Dashboard;

public sealed record OverdueWorkItemDto(Guid Id, string Key, string Title, string Type,
    string Status, string Priority, Guid? AssigneeUserId, DateOnly DueDate, int? EstimateMinutes);
