namespace FlowForge.Domain.Identity;

// refresh session model including expiry, revocation, and replacement.
public sealed class RefreshSession
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public required string TokenHash { get; set; }
    public Guid FamilyId { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public Guid? ReplacedById { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public User User { get; set; } = null!;
}
