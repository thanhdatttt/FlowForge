namespace FlowForge.Api.Contracts.Projects;

public sealed record ProjectMemberResponseDto(Guid UserId, string DisplayName, string Email, string Role);
