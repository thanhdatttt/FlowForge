namespace FlowForge.Api.Contracts.Workspaces;

public sealed record WorkspaceMemberResponseDto(Guid UserId, string Email, string DisplayName, string Role);
