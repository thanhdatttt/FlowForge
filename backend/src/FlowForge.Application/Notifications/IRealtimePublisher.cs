namespace FlowForge.Application.Notifications;

public sealed record RealtimeUpdate(Guid EventId, string Type, Guid WorkspaceId, Guid? ProjectId,
    Guid EntityId, DateTimeOffset OccurredAt);

public interface IRealtimePublisher
{
    Task PublishProjectAsync(RealtimeUpdate update, IReadOnlyList<Guid> recipients, CancellationToken ct);
    Task PublishNotificationAsync(Guid recipientId, Guid notificationId, CancellationToken ct);
}
