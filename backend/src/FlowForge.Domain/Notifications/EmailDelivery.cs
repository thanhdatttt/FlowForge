namespace FlowForge.Domain.Notifications;

public sealed class EmailDelivery
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid WorkspaceId { get; set; }
    public Guid? NotificationId { get; set; }
    public Guid? InvitationId { get; set; }
    public required string RecipientEmail { get; set; }
    public required string Subject { get; set; }
    // Invitation bearer links must never be stored as plaintext in the queue.
    public required string ProtectedBody { get; set; }
    public required string IdempotencyKey { get; set; }
    public string Status { get; set; } = "Pending";
    public int AttemptCount { get; set; }
    public DateTimeOffset NextAttemptAt { get; set; } = DateTimeOffset.UtcNow;
    public Guid? LockId { get; set; }
    public string? SafeError { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAt { get; set; }
}
