namespace FlowForge.Api.Contracts.Projects;

public sealed record UpdateProjectRequestDto(string? Name, string? ProjectKey, string? Description,
    Guid? LeadUserId, DateOnly? StartsOn, DateOnly? TargetOn, long Version);
