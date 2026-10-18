using System.Net;
using System.Net.Http.Json;
using FlowForge.Api.Contracts.Notifications;
using FlowForge.Api.Contracts.WorkItems;
using FlowForge.Api.Contracts.Workflows;
using FlowForge.Domain.Workflows;
using FlowForge.Infrastructure.Workflows;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FlowForge.Api.Tests;

public sealed class PhaseFiveRealtimeTests(PhaseFourFixture fixture) : IClassFixture<PhaseFourFixture>
{
    [PostgresFact]
    public async Task Workflow_notifications_and_execution_events_reach_authorized_websocket_clients_after_commit()
    {
        var journey = new PhaseFourIntegrationTests(fixture);
        var (owner, member, project, path) = await journey.SetupAsync();
        using var client = fixture.Client(owner); using var recipient = fixture.Client(member);
        var workflowPath = $"/api/v1/workspaces/{project.WorkspaceId}/workflows";
        var response = await client.PostAsJsonAsync(workflowPath, new WorkflowWriteDto("Live workflow", project.Id,
            new WorkflowDefinition("WorkItemCreated", [], [new(Guid.NewGuid(), "Notify", member.Id)])));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var workflow = (await response.Content.ReadFromJsonAsync<WorkflowDto>())!;
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync(workflowPath + $"/{workflow.Id}/enable", new WorkflowStateDto(1))).StatusCode);
        await using var socket = await new PhaseFourRealtimeTests(fixture).ConnectAsync(recipient);
        Assert.False((await socket.InvokeAsync("JoinProject", project.WorkspaceId, project.Id)).TryGetProperty("error", out _));
        var item = await PhaseFourIntegrationTests.CreateItemAsync(client, path, null);
        await fixture.InScope(async services =>
        {
            while (await services.GetRequiredService<WorkflowScheduler>().ScheduleNextAsync(default)) { }
            while (await services.GetRequiredService<WorkflowRunner>().RunNextAsync(default)) { }
        });
        await journey.DrainOutboxAsync();
        var notice = (await socket.EventAsync("NotificationReceived")).GetProperty("arguments")[0].GetProperty("id").GetGuid();
        var rest = await recipient.GetFromJsonAsync<PageDto<NotificationDto>>("/api/v1/notifications");
        Assert.Equal("Workflow", Assert.Single(rest!.Items).Type); Assert.Equal(notice, rest.Items[0].Id);
        Assert.Equal(item.Id, rest.Items[0].WorkItemId);
        var receivedOutcome = false;
        for (var i = 0; i < 10; i++)
        {
            var update = (await socket.EventAsync("ProjectUpdated")).GetProperty("arguments")[0];
            if (update.GetProperty("type").GetString() != "WorkflowExecutionUpdated") continue;
            var history = await client.GetFromJsonAsync<WorkflowExecutionDto>(workflowPath + $"/{workflow.Id}/executions/" + update.GetProperty("entityId").GetGuid());
            Assert.Equal("Succeeded", history!.Status);
            Assert.False(update.TryGetProperty("configuration", out _));
            receivedOutcome = true; break;
        }
        Assert.True(receivedOutcome);
        Assert.Equal(HttpStatusCode.NotFound, (await recipient.GetAsync(workflowPath + $"/{workflow.Id}/executions")).StatusCode);
    }
}
