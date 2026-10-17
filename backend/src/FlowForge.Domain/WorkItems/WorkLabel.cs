namespace FlowForge.Domain.WorkItems;

public sealed class WorkLabel
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProjectId { get; set; }
    public required string Name { get; set; }
    public string? Color { get; set; }
}
