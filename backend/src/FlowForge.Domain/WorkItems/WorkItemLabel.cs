namespace FlowForge.Domain.WorkItems;

public sealed class WorkItemLabel
{
    public Guid ProjectId { get; set; }
    public Guid WorkItemId { get; set; }
    public Guid LabelId { get; set; }
}
