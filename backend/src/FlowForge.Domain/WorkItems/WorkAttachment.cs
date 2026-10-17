namespace FlowForge.Domain.WorkItems;

public sealed class WorkAttachment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid WorkItemId { get; set; }
    public Guid UploadedByUserId { get; set; }
    public required string StorageKey { get; set; }
    public required string OriginalName { get; set; }
    public required string ContentType { get; set; }
    public long SizeBytes { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? DeletedAt { get; set; }
}
