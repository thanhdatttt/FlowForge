namespace FlowForge.Domain.Identity;

public sealed class PasswordResetChallenge
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public required string NormalizedEmail { get; set; }
    public required string CodeHash { get; set; }
    public int FailedAttempts { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? ConfirmedAt { get; set; }
    public string? ResetTokenHash { get; set; }
    public DateTimeOffset? ResetTokenExpiresAt { get; set; }
    public DateTimeOffset? UsedAt { get; set; }
    public User User { get; set; } = null!;
}
