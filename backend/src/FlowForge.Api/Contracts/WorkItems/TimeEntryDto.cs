namespace FlowForge.Api.Contracts.WorkItems;

public sealed record TimeEntryDto(Guid Id, Guid UserId, int Minutes, DateTimeOffset? StartedAt, string? Note,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
