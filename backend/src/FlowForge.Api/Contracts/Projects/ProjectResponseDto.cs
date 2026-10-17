namespace FlowForge.Api.Contracts.Projects;

public sealed record ProjectResponseDto(Guid Id, Guid WorkspaceId, string ProjectKey, string Name,
    string? Description, Guid? LeadUserId, DateOnly? StartsOn, DateOnly? TargetOn,
    DateTimeOffset? ArchivedAt, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, long Version);
