namespace FlowForge.Api.Contracts.Workspaces;

public sealed record CreatedInvitationResponseDto(InvitationResponseDto Invitation, string Token, string InvitationUrl);
