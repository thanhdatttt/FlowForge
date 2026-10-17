namespace FlowForge.Domain.WorkItems;

public sealed class WorkItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid WorkspaceId { get; set; }
    public Guid ProjectId { get; set; }
    public long ItemNumber { get; set; }
    public required string Title { get; set; }
    public string? Description { get; set; }
    public required string Type { get; set; }
    public Guid StatusId { get; set; }
    public required string Priority { get; set; }
    public Guid ReporterMembershipId { get; set; }
    public Guid? AssigneeMembershipId { get; set; }
    public Guid? ParentId { get; set; }
    public DateOnly? StartDate { get; set; }
    public DateOnly? DueDate { get; set; }
    public int? EstimateMinutes { get; set; }
    public DateTimeOffset? ArchivedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public long Version { get; set; } = 1;
}
