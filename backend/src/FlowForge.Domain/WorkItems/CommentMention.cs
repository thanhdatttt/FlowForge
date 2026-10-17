namespace FlowForge.Domain.WorkItems;

public sealed class CommentMention
{
    public Guid CommentId { get; set; }
    public Guid MentionedUserId { get; set; }
}
