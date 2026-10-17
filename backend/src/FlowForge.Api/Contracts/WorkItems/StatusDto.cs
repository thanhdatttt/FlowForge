namespace FlowForge.Api.Contracts.WorkItems;

public sealed record StatusDto(Guid Id, string Name, string Category, int SortOrder);
