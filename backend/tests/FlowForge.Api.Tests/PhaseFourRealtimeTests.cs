using System.Net;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using FlowForge.Api.Contracts.Notifications;
using FlowForge.Api.Contracts.WorkItems;
using Xunit;

namespace FlowForge.Api.Tests;

public sealed class PhaseFourRealtimeTests(PhaseFourFixture fixture) : IClassFixture<PhaseFourFixture>
{
    [PostgresFact]
    public async Task Signalr_authorizes_subscriptions_delivers_compact_events_and_recovers_by_refetch_after_reconnect()
    {
        var journey = new PhaseFourIntegrationTests(fixture);
        var (owner, member, project, path) = await journey.SetupAsync();
        var outsider = await fixture.UserAsync();
        using var ownerClient = fixture.Client(owner);
        using var memberClient = fixture.Client(member);
        using var outsiderClient = fixture.Client(outsider);
        using var anonymous = fixture.Client();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsync("/hubs/updates/negotiate?negotiateVersion=1", null)).StatusCode);
        await using var unauthorized = await ConnectAsync(outsiderClient);
        var denied = await unauthorized.InvokeAsync("JoinProject", project.WorkspaceId, project.Id);
        Assert.Contains("Resource not found.", denied.GetProperty("error").GetString());
        var workspaceDenied = await unauthorized.InvokeAsync("JoinWorkspace", project.WorkspaceId);
        Assert.True(workspaceDenied.TryGetProperty("error", out _));
        await using (var socket = await ConnectAsync(memberClient))
        {
            Assert.False((await socket.InvokeAsync("JoinWorkspace", project.WorkspaceId)).TryGetProperty("error", out _));
            Assert.False((await socket.InvokeAsync("JoinProject", project.WorkspaceId, project.Id)).TryGetProperty("error", out _));
            var item = await PhaseFourIntegrationTests.CreateItemAsync(ownerClient, path, member.Id);
            await journey.DrainOutboxAsync();
            var update = await socket.EventAsync("ProjectUpdated");
            var payload = update.GetProperty("arguments")[0];
            Assert.Equal(item.Id, payload.GetProperty("entityId").GetGuid());
            Assert.False(payload.TryGetProperty("payload", out _));
            Assert.False(payload.TryGetProperty("title", out _));
            var notification = await socket.EventAsync("NotificationReceived");
            Assert.NotEqual(Guid.Empty, notification.GetProperty("arguments")[0].GetProperty("id").GetGuid());
            // Overlapping subscriptions use the same event ID, so clients can deduplicate.
            await socket.InvokeAsync("LeaveWorkspace", project.WorkspaceId);
            var eventIds = socket.BufferedEvents.Where(e => e.GetProperty("target").GetString() == "ProjectUpdated")
                .Select(e => e.GetProperty("arguments")[0].GetProperty("eventId").GetGuid())
                .Append(payload.GetProperty("eventId").GetGuid()).Distinct();
            Assert.Single(eventIds);
        }
        // Changes made while disconnected remain available through REST.
        var missed = await PhaseFourIntegrationTests.CreateItemAsync(ownerClient, path, member.Id);
        await journey.DrainOutboxAsync();
        await using var reconnected = await ConnectAsync(memberClient, queryToken: true);
        Assert.False((await reconnected.InvokeAsync("JoinProject", project.WorkspaceId, project.Id)).TryGetProperty("error", out _));
        var current = await memberClient.GetFromJsonAsync<PageDto<WorkItemDto>>(path + "/work-items");
        Assert.Contains(current!.Items, x => x.Id == missed.Id);
        Assert.Equal(2, (await memberClient.GetFromJsonAsync<PageDto<NotificationDto>>("/api/v1/notifications"))!.Total);
        var latest = await PhaseFourIntegrationTests.CreateItemAsync(ownerClient, path, null);
        await journey.DrainOutboxAsync();
        Assert.Equal(latest.Id, (await reconnected.EventAsync("ProjectUpdated")).GetProperty("arguments")[0].GetProperty("entityId").GetGuid());
        Assert.False((await reconnected.InvokeAsync("LeaveProject", project.Id)).TryGetProperty("error", out _));
        Assert.False((await reconnected.InvokeAsync("LeaveWorkspace", project.WorkspaceId)).TryGetProperty("error", out _));
    }

    [PostgresFact]
    public async Task Existing_signalr_connection_stops_receiving_project_events_after_access_revocation()
    {
        var journey = new PhaseFourIntegrationTests(fixture);
        var (owner, member, project, path) = await journey.SetupAsync();
        using var ownerClient = fixture.Client(owner);
        using var memberClient = fixture.Client(member);
        await using var socket = await ConnectAsync(memberClient);
        Assert.False((await socket.InvokeAsync("JoinProject", project.WorkspaceId, project.Id)).TryGetProperty("error", out _));
        Assert.Equal(HttpStatusCode.NoContent, (await ownerClient.DeleteAsync(path + $"/members/{member.Id}")).StatusCode);
        var item = await PhaseFourIntegrationTests.CreateItemAsync(ownerClient, path, null);
        await journey.DrainOutboxAsync();
        var denied = await socket.InvokeAsync("JoinProject", project.WorkspaceId, project.Id);
        Assert.True(denied.TryGetProperty("error", out _));
        Assert.DoesNotContain(socket.BufferedEvents, e => e.GetProperty("target").GetString() == "ProjectUpdated");
        Assert.DoesNotContain(fixture.Realtime.Updates.Where(x => x.Update.EntityId == item.Id), x => x.Recipients.Contains(member.Id));
    }

    [PostgresFact]
    public async Task Leaving_project_and_workspace_stops_delivery_and_workspace_subscriptions_exclude_private_projects()
    {
        var journey = new PhaseFourIntegrationTests(fixture);
        var (owner, member, project, path) = await journey.SetupAsync();
        using var ownerClient = fixture.Client(owner);
        using var memberClient = fixture.Client(member);
        await using var socket = await ConnectAsync(memberClient);
        Assert.True((await socket.InvokeAsync("JoinProject", Guid.NewGuid(), project.Id)).TryGetProperty("error", out _));
        Assert.False((await socket.InvokeAsync("JoinProject", project.WorkspaceId, project.Id)).TryGetProperty("error", out _));
        var first = await PhaseFourIntegrationTests.CreateItemAsync(ownerClient, path, null);
        await journey.DrainOutboxAsync();
        Assert.Equal(first.Id, (await socket.EventAsync("ProjectUpdated")).GetProperty("arguments")[0].GetProperty("entityId").GetGuid());
        Assert.False((await socket.InvokeAsync("LeaveProject", project.Id)).TryGetProperty("error", out _));
        await PhaseFourIntegrationTests.CreateItemAsync(ownerClient, path, null);
        await journey.DrainOutboxAsync();
        await socket.InvokeAsync("LeaveProject", project.Id); // Completion is a barrier after the server sends queued events.
        Assert.DoesNotContain(socket.BufferedEvents, e => e.GetProperty("target").GetString() == "ProjectUpdated");
        Assert.False((await socket.InvokeAsync("JoinWorkspace", project.WorkspaceId)).TryGetProperty("error", out _));
        var visible = await PhaseFourIntegrationTests.CreateItemAsync(ownerClient, path, null);
        await journey.DrainOutboxAsync();
        Assert.Equal(visible.Id, (await socket.EventAsync("ProjectUpdated")).GetProperty("arguments")[0].GetProperty("entityId").GetGuid());
        var privateResponse = await ownerClient.PostAsJsonAsync($"/api/v1/workspaces/{project.WorkspaceId}/projects",
            new { name = "Private project", projectKey = "PRIVATE" });
        Assert.Equal(HttpStatusCode.Created, privateResponse.StatusCode);
        var privateProject = (await privateResponse.Content.ReadFromJsonAsync<FlowForge.Api.Contracts.Projects.ProjectResponseDto>())!;
        await PhaseFourIntegrationTests.CreateItemAsync(ownerClient,
            $"/api/v1/workspaces/{project.WorkspaceId}/projects/{privateProject.Id}", null);
        await journey.DrainOutboxAsync();
        Assert.True((await socket.InvokeAsync("JoinProject", project.WorkspaceId, privateProject.Id)).TryGetProperty("error", out _));
        Assert.DoesNotContain(socket.BufferedEvents, e => e.GetProperty("target").GetString() == "ProjectUpdated");
        Assert.False((await socket.InvokeAsync("LeaveWorkspace", project.WorkspaceId)).TryGetProperty("error", out _));
        await PhaseFourIntegrationTests.CreateItemAsync(ownerClient, path, null);
        await journey.DrainOutboxAsync();
        await socket.InvokeAsync("LeaveWorkspace", project.WorkspaceId);
        Assert.DoesNotContain(socket.BufferedEvents, e => e.GetProperty("target").GetString() == "ProjectUpdated");
        // A later project subscription still works after leaving the workspace.
        Assert.False((await socket.InvokeAsync("JoinProject", project.WorkspaceId, project.Id)).TryGetProperty("error", out _));
        var rejoined = await PhaseFourIntegrationTests.CreateItemAsync(ownerClient, path, null);
        await journey.DrainOutboxAsync();
        Assert.Equal(rejoined.Id, (await socket.EventAsync("ProjectUpdated")).GetProperty("arguments")[0].GetProperty("entityId").GetGuid());
    }

    [PostgresFact]
    public async Task Signalr_comment_events_and_notification_ids_match_committed_rest_resources()
    {
        var journey = new PhaseFourIntegrationTests(fixture);
        var (owner, member, project, path) = await journey.SetupAsync();
        using var ownerClient = fixture.Client(owner);
        using var memberClient = fixture.Client(member);
        await using var socket = await ConnectAsync(memberClient);
        Assert.False((await socket.InvokeAsync("JoinProject", project.WorkspaceId, project.Id)).TryGetProperty("error", out _));
        var item = await PhaseFourIntegrationTests.CreateItemAsync(ownerClient, path, member.Id);
        await journey.DrainOutboxAsync();
        await socket.EventAsync("ProjectUpdated");
        var assignedId = (await socket.EventAsync("NotificationReceived")).GetProperty("arguments")[0].GetProperty("id").GetGuid();
        Assert.Contains((await memberClient.GetFromJsonAsync<PageDto<NotificationDto>>("/api/v1/notifications"))!.Items,
            n => n.Id == assignedId && n.Type == "Assigned" && n.WorkItemId == item.Id);
        var response = await ownerClient.PostAsJsonAsync(path + $"/work-items/{item.Id}/comments",
            new CommentWriteDto($"Hello @{{{member.Id}}}", null));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var comment = (await response.Content.ReadFromJsonAsync<CommentDto>())!;
        await journey.DrainOutboxAsync();
        var added = (await socket.EventAsync("ProjectUpdated")).GetProperty("arguments")[0];
        Assert.Equal("CommentAdded", added.GetProperty("type").GetString());
        Assert.Equal(comment.Id, added.GetProperty("entityId").GetGuid());
        Assert.False(added.TryGetProperty("body", out _));
        var mentionedId = (await socket.EventAsync("NotificationReceived")).GetProperty("arguments")[0].GetProperty("id").GetGuid();
        Assert.Contains((await memberClient.GetFromJsonAsync<PageDto<NotificationDto>>("/api/v1/notifications"))!.Items,
            n => n.Id == mentionedId && n.Type == "Mentioned" && n.CommentId == comment.Id);
        var edit = await ownerClient.PutAsJsonAsync(path + $"/work-items/{item.Id}/comments/{comment.Id}",
            new CommentWriteDto($"Updated @{{{member.Id}}}", comment.Version));
        Assert.Equal(HttpStatusCode.OK, edit.StatusCode);
        await journey.DrainOutboxAsync();
        Assert.Equal("CommentUpdated", (await socket.EventAsync("ProjectUpdated")).GetProperty("arguments")[0].GetProperty("type").GetString());
        Assert.Equal(HttpStatusCode.NoContent, (await ownerClient.DeleteAsync(path + $"/work-items/{item.Id}/comments/{comment.Id}")).StatusCode);
        await journey.DrainOutboxAsync();
        Assert.Equal("CommentDeleted", (await socket.EventAsync("ProjectUpdated")).GetProperty("arguments")[0].GetProperty("type").GetString());
        await socket.InvokeAsync("LeaveProject", project.Id);
        Assert.DoesNotContain(socket.BufferedEvents, e => e.GetProperty("target").GetString() == "NotificationReceived");
    }

    private async Task<HubSocket> ConnectAsync(HttpClient http, bool queryToken = false)
    {
        var client = fixture.Server.CreateWebSocketClient();
        if (!queryToken) client.ConfigureRequest = request => request.Headers.Authorization = http.DefaultRequestHeaders.Authorization!.ToString();
        var token = queryToken ? "?access_token=" + http.DefaultRequestHeaders.Authorization!.Parameter : "";
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var socket = new HubSocket(await client.ConnectAsync(new Uri("ws://localhost/hubs/updates" + token), timeout.Token));
        await socket.HandshakeAsync();
        if (queryToken)
            Assert.DoesNotContain(fixture.Logs.Messages, log => log.Message.Contains(http.DefaultRequestHeaders.Authorization!.Parameter!, StringComparison.Ordinal));
        return socket;
    }

    private sealed class HubSocket(WebSocket socket) : IAsyncDisposable
    {
        private int sequence;
        private string pending = "";
        public List<JsonElement> BufferedEvents { get; } = [];
        public async Task HandshakeAsync()
        {
            await SendAsync(new { protocol = "json", version = 1 });
            Assert.False((await ReadAsync()).TryGetProperty("error", out _));
        }
        public async Task<JsonElement> InvokeAsync(string target, params object[] arguments)
        {
            var id = (++sequence).ToString();
            await SendAsync(new { type = 1, invocationId = id, target, arguments });
            while (true)
            {
                var message = await ReadAsync();
                if (message.TryGetProperty("invocationId", out var received) && received.GetString() == id) return message;
                if (message.TryGetProperty("target", out _)) BufferedEvents.Add(message);
            }
        }
        public async Task<JsonElement> EventAsync(string target)
        {
            while (true)
            {
                var index = BufferedEvents.FindIndex(x => x.GetProperty("target").GetString() == target);
                if (index >= 0)
                {
                    var message = BufferedEvents[index];
                    BufferedEvents.RemoveAt(index);
                    return message;
                }
                var next = await ReadAsync();
                if (next.TryGetProperty("target", out _)) BufferedEvents.Add(next);
            }
        }
        private async Task SendAsync(object message)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(message) + "\u001e");
            await socket.SendAsync(bytes, WebSocketMessageType.Text, true, timeout.Token);
        }
        private async Task<JsonElement> ReadAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            while (true)
            {
                var end = pending.IndexOf('\u001e');
                if (end >= 0)
                {
                    using var json = JsonDocument.Parse(pending[..end]);
                    pending = pending[(end + 1)..];
                    return json.RootElement.Clone();
                }
                var bytes = new byte[8192];
                var received = await socket.ReceiveAsync(bytes.AsMemory(), timeout.Token);
                if (received.MessageType == WebSocketMessageType.Close) throw new InvalidOperationException("Hub closed unexpectedly.");
                pending += Encoding.UTF8.GetString(bytes, 0, received.Count);
            }
        }
        public ValueTask DisposeAsync()
        {
            socket.Abort();
            socket.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
