namespace FlowForge.Api.Contracts.Identity;

public sealed record CurrentUserResponseDto(Guid Id, string Email, string DisplayName);
