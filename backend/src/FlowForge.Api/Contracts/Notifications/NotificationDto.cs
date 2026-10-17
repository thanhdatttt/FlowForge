namespace FlowForge.Api.Contracts.Notifications;

public sealed record NotificationDto(Guid Id, string Type, Guid WorkspaceId, Guid? ProjectId,
    Guid? WorkItemId, Guid? CommentId, Guid? InvitationId, string ResourceUrl,
    DateTimeOffset CreatedAt, DateTimeOffset? ReadAt);
