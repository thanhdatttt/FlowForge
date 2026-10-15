namespace FlowForge.Api.Contracts.Identity;

public sealed record RegisterRequestDto(string? Email, string? Password, string? DisplayName);
