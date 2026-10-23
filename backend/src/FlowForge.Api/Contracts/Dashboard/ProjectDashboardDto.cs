using FlowForge.Api.Contracts.WorkItems;

namespace FlowForge.Api.Contracts.Dashboard;

public sealed record ProjectDashboardDto(Guid ProjectId, DateTimeOffset AsOf, DateOnly TodayUtc,
    int TotalItems, int CompletedItems, decimal CompletionPercentage, int OverdueItems,
    IReadOnlyList<StatusCountDto> CountsByStatus, IReadOnlyList<PriorityCountDto> CountsByPriority,
    IReadOnlyList<ActivityDto> RecentActivity);
