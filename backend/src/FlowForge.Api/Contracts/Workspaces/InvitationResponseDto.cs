namespace FlowForge.Api.Contracts.Workspaces;

public sealed record InvitationResponseDto(Guid Id, Guid WorkspaceId, string Email, string Role,
    string Status, DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt);
