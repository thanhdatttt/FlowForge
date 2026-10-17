using FlowForge.Application.Notifications;
using Microsoft.AspNetCore.SignalR;

namespace FlowForge.Api.Realtime;

public sealed class SignalRPublisher(IHubContext<UpdatesHub> hub) : IRealtimePublisher
{
    public Task PublishProjectAsync(RealtimeUpdate update, IReadOnlyList<Guid> recipients, CancellationToken ct) =>
        hub.Clients.Groups(recipients.SelectMany(user => new[] { UpdatesHub.ProjectGroup(update.ProjectId!.Value, user),
            UpdatesHub.WorkspaceGroup(update.WorkspaceId, user) }).ToList())
            .SendAsync("ProjectUpdated", update, ct);
    public Task PublishNotificationAsync(Guid recipientId, Guid notificationId, CancellationToken ct) =>
        hub.Clients.Group(UpdatesHub.UserGroup(recipientId)).SendAsync("NotificationReceived", new { id = notificationId }, ct);
}
