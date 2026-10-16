namespace FlowForge.Domain.Workspaces;

public sealed class WorkspaceInvitation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid WorkspaceId { get; set; }
    public required string NormalizedEmail { get; set; }
    public WorkspaceRole Role { get; set; }
    public required string TokenHash { get; set; }
    public Guid InvitedByUserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? AcceptedAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public string Status(DateTimeOffset now) => AcceptedAt != null ? "Accepted" :
        RevokedAt != null && RevokedAt < ExpiresAt ? "Revoked" : ExpiresAt <= now ? "Expired" : "Pending";
}
