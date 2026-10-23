namespace FlowForge.Api.Contracts.Dashboard;

public sealed record AssigneeWorkloadDto(Guid? AssigneeUserId, string DisplayName,
    int OpenItems, long EstimateMinutes);
