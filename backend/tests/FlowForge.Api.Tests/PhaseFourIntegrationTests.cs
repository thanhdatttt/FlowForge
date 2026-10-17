using System.Net;
using System.Net.Http.Json;
using FlowForge.Api.Contracts.Notifications;
using FlowForge.Api.Contracts.Projects;
using FlowForge.Api.Contracts.WorkItems;
using FlowForge.Api.Contracts.Workspaces;
using FlowForge.Application.Notifications;
using FlowForge.Domain.Identity;
using FlowForge.Domain.Projects;
using FlowForge.Domain.WorkItems;
using FlowForge.Domain.Workspaces;
using FlowForge.Infrastructure;
using FlowForge.Infrastructure.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace FlowForge.Api.Tests;

public sealed class PhaseFourIntegrationTests(PhaseFourFixture fixture) : IClassFixture<PhaseFourFixture>
{
    [PostgresFact]
    public async Task Assignment_mentions_read_apis_and_email_are_idempotent_and_authorized()
    {
        var (owner, member, project, path) = await SetupAsync();
        using var client = fixture.Client(owner);
        using var recipient = fixture.Client(member);
        using var anonymous = fixture.Client();
        var item = await CreateItemAsync(client, path, member.Id);
        await DrainOutboxAsync();
        var notifications = await recipient.GetFromJsonAsync<PageDto<NotificationDto>>("/api/v1/notifications?unreadOnly=true");
        var assigned = Assert.Single(notifications!.Items);
        Assert.Equal("Assigned", assigned.Type);
        Assert.Equal(item.Id, assigned.WorkItemId);
        Assert.Equal(path + $"/work-items/{item.Id}", assigned.ResourceUrl);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/v1/notifications")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsync($"/api/v1/notifications/{assigned.Id}/read", null)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await recipient.GetAsync("/api/v1/notifications?page=0&pageSize=101")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await recipient.PostAsync($"/api/v1/notifications/{assigned.Id}/read", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await recipient.PostAsync($"/api/v1/notifications/{assigned.Id}/read", null)).StatusCode);
        Assert.Empty((await recipient.GetFromJsonAsync<PageDto<NotificationDto>>("/api/v1/notifications?unreadOnly=true"))!.Items);
        var commentResponse = await client.PostAsJsonAsync(path + $"/work-items/{item.Id}/comments",
            new CommentWriteDto($"Hello @{{{member.Id}}} @{{{owner.Id}}}", null));
        Assert.Equal(HttpStatusCode.Created, commentResponse.StatusCode);
        await DrainOutboxAsync();
        Assert.Equal(HttpStatusCode.NoContent, (await recipient.PostAsync("/api/v1/notifications/read-all", null)).StatusCode);
        var all = await recipient.GetFromJsonAsync<PageDto<NotificationDto>>("/api/v1/notifications");
        Assert.Equal(2, all!.Total);
        Assert.All(all.Items, n => Assert.NotNull(n.ReadAt));
        Assert.Empty((await client.GetFromJsonAsync<PageDto<NotificationDto>>("/api/v1/notifications"))!.Items);
        await fixture.InDb(async db =>
        {
            var e = await db.DomainEvents.SingleAsync(x => x.ProjectId == project.Id && x.EventType == "WorkItemCreated");
            await db.OutboxMessages.Where(x => x.EventId == e.Id).ExecuteUpdateAsync(s => s
                .SetProperty(x => x.ProcessedAt, (DateTimeOffset?)null).SetProperty(x => x.AvailableAt, fixture.Clock.GetUtcNow()));
        });
        await DrainOutboxAsync();
        await fixture.InDb(async db => Assert.Equal(2, await db.Notifications.CountAsync(x => x.ProjectId == project.Id)));
        await DrainEmailAsync();
        Assert.Equal(2, fixture.Sender.Messages.Count(x => x.Recipient == member.Email));
        Assert.Contains(fixture.Realtime.Notifications, x => x.Recipient == member.Id && x.Notification == assigned.Id);
    }

    [PostgresFact]
    public async Task Comment_edits_notify_only_new_mentions_and_deletions_cancel_pending_email()
    {
        var (owner, member, _, path) = await SetupAsync();
        using var client = fixture.Client(owner);
        using var recipient = fixture.Client(member);
        var item = await CreateItemAsync(client, path, null);
        var response = await client.PostAsJsonAsync(path + $"/work-items/{item.Id}/comments", new CommentWriteDto("Initial", null));
        var comment = (await response.Content.ReadFromJsonAsync<CommentDto>())!;
        var update = await client.PutAsJsonAsync(path + $"/work-items/{item.Id}/comments/{comment.Id}",
            new CommentWriteDto($"Added @{{{member.Id}}}", comment.Version));
        var revised = (await update.Content.ReadFromJsonAsync<CommentDto>())!;
        await DrainOutboxAsync();
        Assert.Single((await recipient.GetFromJsonAsync<PageDto<NotificationDto>>("/api/v1/notifications"))!.Items);
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync(path + $"/work-items/{item.Id}/comments/{comment.Id}",
            new CommentWriteDto($"Still @{{{member.Id}}}", revised.Version))).StatusCode);
        await DrainOutboxAsync();
        Assert.Single((await recipient.GetFromJsonAsync<PageDto<NotificationDto>>("/api/v1/notifications"))!.Items);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync(path + $"/work-items/{item.Id}/comments/{comment.Id}")).StatusCode);
        await DrainOutboxAsync();
        await DrainEmailAsync();
        Assert.DoesNotContain(fixture.Sender.Messages, x => x.Recipient == member.Email);
        await fixture.InDb(async db => Assert.Equal("Cancelled", (await db.EmailDeliveries.SingleAsync(x =>
            db.Notifications.Any(n => n.Id == x.NotificationId && n.CommentId == comment.Id))).Status));
    }

    [PostgresFact]
    public async Task Overdue_checks_use_utc_skip_completed_archived_and_unassigned_and_deduplicate_concurrent_runs()
    {
        var (owner, member, project, path) = await SetupAsync();
        using var client = fixture.Client(owner);
        var today = DateOnly.FromDateTime(fixture.Clock.GetUtcNow().UtcDateTime);
        var overdue = await CreateItemAsync(client, path, member.Id, today.AddDays(-1));
        var todayItem = await CreateItemAsync(client, path, member.Id, today);
        await CreateItemAsync(client, path, null, today.AddDays(-1));
        var completed = await CreateItemAsync(client, path, member.Id, today.AddDays(-1), "Done");
        var archived = await CreateItemAsync(client, path, member.Id, today.AddDays(-1));
        await client.PostAsync(path + $"/work-items/{archived.Id}/archive", null);
        await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => fixture.InScope(async services =>
            await services.GetRequiredService<OverdueChecker>().CheckAsync(default))));
        await fixture.InDb(async db =>
        {
            var n = Assert.Single(await db.Notifications.Where(x => x.ProjectId == project.Id && x.Type == "Overdue").ToListAsync());
            Assert.Equal(overdue.Id, n.WorkItemId);
            Assert.NotEqual(completed.Id, n.WorkItemId);
            Assert.NotEqual(todayItem.Id, n.WorkItemId);
        });
        await fixture.InScope(async services => Assert.Equal(0, await services.GetRequiredService<OverdueChecker>().CheckAsync(default)));
        await fixture.InDb(async db =>
        {
            var item = await db.WorkItems.SingleAsync(x => x.Id == overdue.Id);
            item.DueDate = today.AddDays(-2);
            await db.SaveChangesAsync();
        });
        await fixture.InScope(async services => Assert.Equal(1, await services.GetRequiredService<OverdueChecker>().CheckAsync(default)));
        await fixture.InDb(async db => Assert.Equal(2, await db.Notifications.CountAsync(x => x.ProjectId == project.Id && x.Type == "Overdue")));
        await client.PostAsync(path + "/archive", null);
        fixture.Clock.Advance(TimeSpan.FromDays(1));
        await fixture.InScope(async services => Assert.Equal(0, await services.GetRequiredService<OverdueChecker>().CheckAsync(default)));
    }

    [PostgresFact]
    public async Task Invitation_email_is_encrypted_background_delivery_and_revocation_hides_notification()
    {
        var owner = await fixture.UserAsync();
        var invited = await fixture.UserAsync();
        var workspace = await fixture.WorkspaceAsync(owner);
        using var client = fixture.Client(owner);
        using var recipient = fixture.Client(invited);
        var response = await client.PostAsJsonAsync($"/api/v1/workspaces/{workspace.Id}/invitations",
            new CreateInvitationRequestDto(invited.Email, "Developer"));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = (await response.Content.ReadFromJsonAsync<CreatedInvitationResponseDto>())!;
        await fixture.InScope(async services =>
        {
            var db = services.GetRequiredService<FlowForgeDbContext>();
            var delivery = await db.EmailDeliveries.SingleAsync(x => x.InvitationId == created.Invitation.Id);
            Assert.DoesNotContain(created.Token, delivery.ProtectedBody);
            Assert.Contains(created.InvitationUrl, services.GetRequiredService<IEmailContentProtector>().Unprotect(delivery.ProtectedBody));
            Assert.DoesNotContain(created.Token, (await db.DomainEvents.SingleAsync(x => x.EntityId == created.Invitation.Id)).Payload);
        });
        Assert.DoesNotContain(fixture.Sender.Messages, x => string.Equals(x.Recipient, invited.Email, StringComparison.OrdinalIgnoreCase));
        await DrainOutboxAsync();
        Assert.Equal("Invited", Assert.Single((await recipient.GetFromJsonAsync<PageDto<NotificationDto>>("/api/v1/notifications"))!.Items).Type);
        await DrainEmailAsync();
        Assert.Contains(fixture.Sender.Messages, x => string.Equals(x.Recipient, invited.Email, StringComparison.OrdinalIgnoreCase) && x.Body.Contains(created.InvitationUrl));
        await client.PostAsync($"/api/v1/workspaces/{workspace.Id}/invitations/{created.Invitation.Id}/revoke", null);
        Assert.Empty((await recipient.GetFromJsonAsync<PageDto<NotificationDto>>("/api/v1/notifications"))!.Items);
        await fixture.InDb(async db => Assert.Empty((await db.EmailDeliveries.SingleAsync(x => x.InvitationId == created.Invitation.Id)).ProtectedBody));
        var pending = await client.PostAsJsonAsync($"/api/v1/workspaces/{workspace.Id}/invitations",
            new CreateInvitationRequestDto(invited.Email, "Developer"));
        var second = (await pending.Content.ReadFromJsonAsync<CreatedInvitationResponseDto>())!;
        await client.PostAsync($"/api/v1/workspaces/{workspace.Id}/invitations/{second.Invitation.Id}/revoke", null);
        await DrainEmailAsync();
        await fixture.InDb(async db => Assert.Equal("Cancelled", (await db.EmailDeliveries.SingleAsync(x => x.InvitationId == second.Invitation.Id)).Status));
    }

    [PostgresFact]
    public async Task Removed_members_cannot_read_or_receive_queued_project_notifications()
    {
        var (owner, member, project, path) = await SetupAsync();
        using var client = fixture.Client(owner);
        using var recipient = fixture.Client(member);
        var item = await CreateItemAsync(client, path, member.Id);
        await DrainOutboxAsync();
        var n = Assert.Single((await recipient.GetFromJsonAsync<PageDto<NotificationDto>>("/api/v1/notifications"))!.Items);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync(path + $"/members/{member.Id}")).StatusCode);
        Assert.Empty((await recipient.GetFromJsonAsync<PageDto<NotificationDto>>("/api/v1/notifications"))!.Items);
        Assert.Equal(HttpStatusCode.NotFound, (await recipient.PostAsync($"/api/v1/notifications/{n.Id}/read", null)).StatusCode);
        await DrainEmailAsync();
        Assert.DoesNotContain(fixture.Sender.Messages, x => x.Recipient == member.Email);
        var updated = await client.PutAsJsonAsync(path + $"/work-items/{item.Id}", Write(null) with { Version = item.Version });
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        await DrainOutboxAsync();
        Assert.DoesNotContain(fixture.Realtime.Updates.Where(x => x.Update.ProjectId == project.Id && x.Update.Type == "WorkItemUpdated"),
            x => x.Recipients.Contains(member.Id));
    }

    [PostgresFact]
    public async Task Notification_endpoints_enforce_authentication_paging_and_read_all_recipient_isolation()
    {
        var (owner, member, _, path) = await SetupAsync();
        using var ownerClient = fixture.Client(owner);
        using var memberClient = fixture.Client(member);
        using var anonymous = fixture.Client();
        for (var i = 0; i < 3; i++) await CreateItemAsync(ownerClient, path, member.Id);
        await CreateItemAsync(memberClient, path, owner.Id);
        await DrainOutboxAsync();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/v1/notifications")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsync($"/api/v1/notifications/{Guid.NewGuid()}/read", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsync("/api/v1/notifications/read-all", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/v1/notifications?access_token=" +
            memberClient.DefaultRequestHeaders.Authorization!.Parameter)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await memberClient.GetAsync("/api/v1/notifications?page=abc")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await memberClient.GetAsync("/api/v1/notifications?unreadOnly=invalid")).StatusCode);
        var first = (await memberClient.GetFromJsonAsync<PageDto<NotificationDto>>("/api/v1/notifications?page=1&pageSize=1"))!;
        var second = (await memberClient.GetFromJsonAsync<PageDto<NotificationDto>>("/api/v1/notifications?page=2&pageSize=1"))!;
        Assert.Equal(3, first.Total);
        Assert.Equal(1, first.Page);
        Assert.Equal(1, first.PageSize);
        Assert.NotEqual(Assert.Single(first.Items).Id, Assert.Single(second.Items).Id);
        Assert.Equal(HttpStatusCode.NoContent, (await memberClient.PostAsync($"/api/v1/notifications/{first.Items[0].Id}/read", null)).StatusCode);
        Assert.Equal(2, (await memberClient.GetFromJsonAsync<PageDto<NotificationDto>>("/api/v1/notifications?unreadOnly=true"))!.Total);
        Assert.Equal(HttpStatusCode.NotFound, (await memberClient.PostAsync($"/api/v1/notifications/{Guid.NewGuid()}/read", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await memberClient.PostAsync("/api/v1/notifications/read-all", null)).StatusCode);
        var read = (await memberClient.GetFromJsonAsync<PageDto<NotificationDto>>("/api/v1/notifications"))!;
        Assert.Equal(3, read.Total);
        Assert.All(read.Items, n => Assert.NotNull(n.ReadAt));
        Assert.Equal(HttpStatusCode.NoContent, (await memberClient.PostAsync("/api/v1/notifications/read-all", null)).StatusCode);
        var repeated = (await memberClient.GetFromJsonAsync<PageDto<NotificationDto>>("/api/v1/notifications"))!;
        Assert.Equal(read.Items.Select(n => n.ReadAt), repeated.Items.Select(n => n.ReadAt));
        var otherRecipient = (await ownerClient.GetFromJsonAsync<PageDto<NotificationDto>>("/api/v1/notifications?unreadOnly=true"))!;
        Assert.Single(otherRecipient.Items);
        Assert.Null(otherRecipient.Items[0].ReadAt);
        Assert.Equal(HttpStatusCode.NotFound, (await memberClient.PostAsync($"/api/v1/notifications/{otherRecipient.Items[0].Id}/read", null)).StatusCode);
        await fixture.InDb(async db =>
        {
            await db.Users.Where(u => u.Id == member.Id).ExecuteUpdateAsync(setters => setters.SetProperty(u => u.IsActive, false));
        });
        Assert.Equal(HttpStatusCode.Unauthorized, (await memberClient.GetAsync("/api/v1/notifications")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await memberClient.PostAsync("/api/v1/notifications/read-all", null)).StatusCode);
    }

    [PostgresFact]
    public async Task Empty_notification_id_returns_field_validation_problem_before_resource_lookup()
    {
        var user = await fixture.UserAsync();
        using var client = fixture.Client(user);
        var response = await client.PostAsync($"/api/v1/notifications/{Guid.Empty}/read", null);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType!.MediaType);
        using var problem = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(problem.RootElement.GetProperty("errors").TryGetProperty("notificationId", out _));
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsync($"/api/v1/notifications/{Guid.NewGuid()}/read", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync("/api/v1/notifications/read-all", null)).StatusCode);
    }

    internal async Task DrainOutboxAsync()
    {
        await fixture.InScope(async services =>
        {
            for (var i = 0; i < 500; i++)
                if (!await services.GetRequiredService<NotificationDispatcher>().DispatchNextAsync(default)) return;
            Assert.Fail("Outbox did not drain.");
        });
    }
    private async Task DrainEmailAsync() => await fixture.InScope(async services =>
    {
        for (var i = 0; i < 500; i++)
            if (!await services.GetRequiredService<EmailDeliveryProcessor>().SendNextAsync(default)) return;
        Assert.Fail("Email queue did not drain.");
    });
    internal async Task<(User Owner, User Member, ProjectResponseDto Project, string Path)> SetupAsync()
    {
        var owner = await fixture.UserAsync();
        var member = await fixture.UserAsync();
        var workspace = await fixture.WorkspaceAsync(owner, (member, WorkspaceRole.Developer));
        using var client = fixture.Client(owner);
        var response = await client.PostAsJsonAsync($"/api/v1/workspaces/{workspace.Id}/projects",
            new { name = "Phase Four", projectKey = "LIVE" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var project = (await response.Content.ReadFromJsonAsync<ProjectResponseDto>())!;
        await fixture.InDb(async db =>
        {
            var membership = await db.WorkspaceMemberships.SingleAsync(x => x.WorkspaceId == workspace.Id && x.UserId == member.Id);
            db.ProjectMemberships.Add(new ProjectMembership { WorkspaceId = workspace.Id, ProjectId = project.Id, WorkspaceMembershipId = membership.Id });
            await db.SaveChangesAsync();
        });
        return (owner, member, project, $"/api/v1/workspaces/{workspace.Id}/projects/{project.Id}");
    }
    internal static WorkItemWriteDto Write(Guid? assignee, DateOnly? due = null, string status = "Todo") =>
        new("Phase Four item", null, "Task", status, "Medium", assignee, null, null, due, null, [], null);
    internal static async Task<WorkItemDto> CreateItemAsync(HttpClient client, string path, Guid? assignee, DateOnly? due = null, string status = "Todo")
    {
        var response = await client.PostAsJsonAsync(path + "/work-items", Write(assignee, due, status));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<WorkItemDto>())!;
    }
}
