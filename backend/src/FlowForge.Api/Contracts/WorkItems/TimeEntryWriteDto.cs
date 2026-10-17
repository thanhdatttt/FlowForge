namespace FlowForge.Api.Contracts.WorkItems;

public sealed record TimeEntryWriteDto(int Minutes, DateTimeOffset? StartedAt, string? Note);
