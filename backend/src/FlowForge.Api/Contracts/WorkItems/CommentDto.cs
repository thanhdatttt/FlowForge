namespace FlowForge.Api.Contracts.WorkItems;

public sealed record CommentDto(Guid Id, Guid AuthorUserId, string Body, IReadOnlyList<Guid> MentionedUserIds,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, long Version);
