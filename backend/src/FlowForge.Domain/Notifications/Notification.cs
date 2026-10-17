namespace FlowForge.Domain.Notifications;

public sealed class Notification
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RecipientUserId { get; set; }
    public Guid WorkspaceId { get; set; }
    public Guid? ProjectId { get; set; }
    public Guid? WorkItemId { get; set; }
    public Guid? CommentId { get; set; }
    public Guid? InvitationId { get; set; }
    public DateOnly? DueDate { get; set; }
    public required string Type { get; set; }
    public required string IdempotencyKey { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ReadAt { get; set; }
}
