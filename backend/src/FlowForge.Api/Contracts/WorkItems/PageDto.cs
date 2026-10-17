namespace FlowForge.Api.Contracts.WorkItems;

public sealed record PageDto<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize);
