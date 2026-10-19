namespace FlowForge.Api.Contracts.Identity;

public sealed record ForgotPasswordRequestDto(string? Email);
public sealed record VerifyPasswordResetRequestDto(string? Email, string? Code);
public sealed record PasswordResetTokenResponseDto(string ResetToken, DateTimeOffset ExpiresAt);
public sealed record ResetPasswordRequestDto(string? ResetToken, string? Password);
