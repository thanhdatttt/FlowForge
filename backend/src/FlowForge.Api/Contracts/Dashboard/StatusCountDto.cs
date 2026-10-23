namespace FlowForge.Api.Contracts.Dashboard;

public sealed record StatusCountDto(Guid StatusId, string Name, string Category, int Count);
