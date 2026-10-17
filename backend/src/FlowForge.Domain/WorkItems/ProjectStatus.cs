namespace FlowForge.Domain.WorkItems;

public sealed class ProjectStatus
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProjectId { get; set; }
    public required string Name { get; set; }
    public required string Category { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
}
