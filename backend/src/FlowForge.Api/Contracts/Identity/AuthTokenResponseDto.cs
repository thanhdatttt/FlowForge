namespace FlowForge.Api.Contracts.Identity;

public sealed record AuthTokenResponseDto(
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAt,
    string RefreshToken);
