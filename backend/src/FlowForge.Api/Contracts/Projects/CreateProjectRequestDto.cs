namespace FlowForge.Api.Contracts.Projects;

public sealed record CreateProjectRequestDto(string? Name, string? ProjectKey, string? Description = null,
    Guid? LeadUserId = null, DateOnly? StartsOn = null, DateOnly? TargetOn = null);
