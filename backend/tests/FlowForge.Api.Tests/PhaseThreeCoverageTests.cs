using System.Net;
using System.Net.Http.Json;
using FlowForge.Api.Contracts.Projects;
using FlowForge.Api.Contracts.WorkItems;
using FlowForge.Domain.Projects;
using FlowForge.Domain.Workspaces;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FlowForge.Api.Tests;

public sealed class PhaseThreeCoverageTests(PhaseTwoFixture fixture) : IClassFixture<PhaseTwoFixture>
{
    [PostgresFact]
    public async Task Remaining_phase_three_endpoint_operations_complete_a_project_journey()
    {
        var owner = await fixture.UserAsync();
        var workspace = await fixture.WorkspaceAsync(owner);
        using var client = fixture.Client(owner);
        var project = await ProjectAsync(client, workspace.Id, "JOURNEY");
        var path = BasePath(workspace.Id, project.Id);

        var statuses = await client.GetFromJsonAsync<List<StatusDto>>(path + "/statuses");
        Assert.Equal(new[] { "Todo", "InProgress", "InReview", "Done", "Blocked" },
            statuses!.Select(x => x.Name));
        var label = await LabelAsync(client, path, "Initial");
        Assert.Contains((await client.GetFromJsonAsync<List<LabelDto>>(path + "/labels"))!, x => x.Id == label.Id);
        var renamed = await client.PutAsJsonAsync(path + $"/labels/{label.Id}", new LabelWriteDto("Renamed", "#654321"));
        Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);
        Assert.Equal("Renamed", (await renamed.Content.ReadFromJsonAsync<LabelDto>())!.Name);

        var prerequisite = await ItemAsync(client, path, "Prerequisite", "Task", "Medium", null, null, []);
        var item = await ItemAsync(client, path, "Main", "Issue", "High", null, null, [label.Id]);
        var itemPath = path + $"/work-items/{item.Id}";
        Assert.Equal(item.Id, (await client.GetFromJsonAsync<WorkItemDto>(itemPath))!.Id);
        Assert.Contains((await client.GetFromJsonAsync<PageDto<WorkItemDto>>(path + "/work-items"))!.Items,
            x => x.Id == item.Id);
        var added = await client.PostAsJsonAsync(itemPath + "/dependencies", new DependencyWriteDto(prerequisite.Id));
        Assert.Equal(HttpStatusCode.Created, added.StatusCode);
        Assert.True((await client.GetFromJsonAsync<WorkItemDto>(itemPath))!.IsBlocked);
        Assert.Equal(HttpStatusCode.NoContent,
            (await client.DeleteAsync(itemPath + $"/dependencies/{prerequisite.Id}")).StatusCode);
        var current = await client.GetFromJsonAsync<WorkItemDto>(itemPath);
        Assert.False(current!.IsBlocked);
        var updated = await client.PutAsJsonAsync(itemPath,
            Write("Main updated", "Issue", "InProgress", "High", null, null, [label.Id], current.Version));
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);

        var commentResponse = await client.PostAsJsonAsync(itemPath + "/comments", new CommentWriteDto("Initial", null));
        Assert.Equal(HttpStatusCode.Created, commentResponse.StatusCode);
        var comment = (await commentResponse.Content.ReadFromJsonAsync<CommentDto>())!;
        Assert.Contains((await client.GetFromJsonAsync<PageDto<CommentDto>>(itemPath + "/comments"))!.Items,
            x => x.Id == comment.Id);
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync(itemPath + $"/comments/{comment.Id}",
            new CommentWriteDto("Revised", comment.Version))).StatusCode);

        var timeResponse = await client.PostAsJsonAsync(itemPath + "/time-entries",
            new TimeEntryWriteDto(15, null, "Start"));
        Assert.Equal(HttpStatusCode.Created, timeResponse.StatusCode);
        var entry = (await timeResponse.Content.ReadFromJsonAsync<TimeEntryDto>())!;
        Assert.Contains((await client.GetFromJsonAsync<PageDto<TimeEntryDto>>(itemPath + "/time-entries"))!.Items,
            x => x.Id == entry.Id);
        var timeUpdate = await client.PutAsJsonAsync(itemPath + $"/time-entries/{entry.Id}",
            new TimeEntryWriteDto(30, null, "Finish"));
        Assert.Equal(HttpStatusCode.OK, timeUpdate.StatusCode);
        Assert.Equal(30, (await timeUpdate.Content.ReadFromJsonAsync<TimeEntryDto>())!.Minutes);

        using var multipart = new MultipartFormDataContent();
        var file = new ByteArrayContent("%PDF-1.7 endpoint journey"u8.ToArray());
        file.Headers.ContentType = new("application/pdf");
        multipart.Add(file, "file", "journey.pdf");
        var upload = await client.PostAsync(itemPath + "/attachments", multipart);
        Assert.Equal(HttpStatusCode.Created, upload.StatusCode);
        var attachment = (await upload.Content.ReadFromJsonAsync<AttachmentDto>())!;
        Assert.Contains((await client.GetFromJsonAsync<PageDto<AttachmentDto>>(itemPath + "/attachments"))!.Items,
            x => x.Id == attachment.Id);
        Assert.Equal(HttpStatusCode.OK,
            (await client.GetAsync(itemPath + $"/attachments/{attachment.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent,
            (await client.DeleteAsync(itemPath + $"/attachments/{attachment.Id}")).StatusCode);

        var feed = await client.GetFromJsonAsync<PageDto<ActivityDto>>(
            $"/api/v1/workspaces/{workspace.Id}/activity?projectId={project.Id}&workItemId={item.Id}");
        Assert.Contains(feed!.Items, x => x.Action == "dependency.removed");
        Assert.Contains(feed.Items, x => x.Action == "time-entry.updated");
        Assert.Equal(HttpStatusCode.NoContent,
            (await client.DeleteAsync(itemPath + $"/time-entries/{entry.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent,
            (await client.DeleteAsync(itemPath + $"/comments/{comment.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync(itemPath + "/archive", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync(itemPath + "/restore", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent,
            (await client.DeleteAsync(path + $"/labels/{label.Id}")).StatusCode);
        Assert.Empty((await client.GetFromJsonAsync<WorkItemDto>(itemPath))!.LabelIds);
    }

    [PostgresFact]
    public async Task Work_item_filters_activity_details_and_outbox_follow_committed_changes()
    {
        var owner = await fixture.UserAsync();
        var developer = await fixture.UserAsync();
        var outsider = await fixture.UserAsync();
        var workspace = await fixture.WorkspaceAsync(owner, (developer, WorkspaceRole.Developer));
        using var client = fixture.Client(owner);
        using var developerClient = fixture.Client(developer);
        using var outsiderClient = fixture.Client(outsider);
        var project = await ProjectAsync(client, workspace.Id, "AUDIT");
        await AddProjectMemberAsync(workspace.Id, project.Id, developer.Id);
        var path = BasePath(workspace.Id, project.Id);
        var firstLabel = await LabelAsync(client, path, "Backend");
        var secondLabel = await LabelAsync(client, path, "Review");
        var first = await ItemAsync(developerClient, path, "Build API", "Task", "High", developer.Id,
            new DateOnly(2026, 11, 1), [firstLabel.Id]);
        var second = await ItemAsync(client, path, "Investigate bug", "Issue", "Low", null,
            new DateOnly(2026, 12, 1), [secondLabel.Id]);
        var filtered = await client.GetFromJsonAsync<PageDto<WorkItemDto>>(path +
            $"/work-items?status=Todo&priority=High&type=Task&assigneeUserId={developer.Id}&labelId={firstLabel.Id}&dueBefore=2026-11-05&dueAfter=2026-10-25&sort=priority");
        Assert.Equal(1, filtered!.Total);
        Assert.Equal(first.Id, Assert.Single(filtered.Items).Id);
        var byReporter = await client.GetFromJsonAsync<PageDto<WorkItemDto>>(path +
            $"/work-items?reporterUserId={owner.Id}&search=bug");
        Assert.Equal(second.Id, Assert.Single(byReporter!.Items).Id);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync(path +
            "/work-items?dueAfter=2026-12-01&dueBefore=2026-11-01")).StatusCode);
        var update = Write("Build API reviewed", "Task", "Done", "Critical", owner.Id,
            new DateOnly(2026, 11, 2), [secondLabel.Id], first.Version);
        var updateResponse = await developerClient.PutAsJsonAsync(path + $"/work-items/{first.Id}", update);
        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
        var updated = await updateResponse.Content.ReadFromJsonAsync<WorkItemDto>();
        Assert.Equal("Done", updated!.Status);
        Assert.Equal(owner.Id, updated.AssigneeUserId);
        var commentResponse = await developerClient.PostAsJsonAsync(path + $"/work-items/{first.Id}/comments",
            new CommentWriteDto("Review complete", null));
        Assert.Equal(HttpStatusCode.Created, commentResponse.StatusCode);
        var activityUrl = $"/api/v1/workspaces/{workspace.Id}/activity?projectId={project.Id}&workItemId={first.Id}";
        var activity = await client.GetFromJsonAsync<PageDto<ActivityDto>>(activityUrl);
        Assert.Equal(3, activity!.Total);
        var changed = Assert.Single(activity.Items, x => x.Action == "work-item.updated");
        Assert.Equal(first.Id, changed.WorkItemId);
        Assert.Contains(firstLabel.Id, changed.BeforeValues!.Value.GetProperty("LabelIds").EnumerateArray().Select(x => x.GetGuid()));
        Assert.Contains(secondLabel.Id, changed.AfterValues!.Value.GetProperty("LabelIds").EnumerateArray().Select(x => x.GetGuid()));
        Assert.Contains(activity.Items, x => x.Action == "comment.created" && x.WorkItemId == first.Id);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync($"/api/v1/workspaces/{workspace.Id}/activity?workItemId={first.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await outsiderClient.GetAsync(activityUrl)).StatusCode);
        await fixture.InDb(async db =>
        {
            var events = await db.DomainEvents.Where(x => x.ProjectId == project.Id).ToListAsync();
            Assert.Equal(6, events.Count);
            Assert.Contains(events, x => x.EventType == "WorkItemCreated");
            Assert.Contains(events, x => x.EventType == "WorkItemUpdated");
            Assert.Contains(events, x => x.EventType == "WorkItemStatusChanged");
            Assert.Contains(events, x => x.EventType == "WorkItemAssignmentChanged");
            Assert.Contains(events, x => x.EventType == "CommentAdded");
            Assert.All(events, x => Assert.DoesNotContain("Review complete", x.Payload));
            var ids = events.Select(x => x.Id).ToArray();
            Assert.Equal(events.Count, await db.OutboxMessages.CountAsync(x => ids.Contains(x.EventId) &&
                x.ProcessedAt == null && x.AttemptCount == 0));
        });
    }

    [PostgresFact]
    public async Task Labels_relationships_and_activity_respect_project_scope()
    {
        var owner = await fixture.UserAsync();
        var workspace = await fixture.WorkspaceAsync(owner);
        using var client = fixture.Client(owner);
        var project = await ProjectAsync(client, workspace.Id, "MAIN");
        var other = await ProjectAsync(client, workspace.Id, "OTHER");
        var path = BasePath(workspace.Id, project.Id);
        var otherPath = BasePath(workspace.Id, other.Id);
        var label = await LabelAsync(client, path, "Urgent");
        var item = await ItemAsync(client, path, "Parent", "Task", "Medium", null, null, [label.Id]);
        var otherItem = await ItemAsync(client, otherPath, "Elsewhere", "Task", "Medium", null, null, []);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(path + "/work-items",
            Write("Invalid labels", "Task", "Todo", "Medium", null, null, [label.Id, label.Id], null))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(path + "/work-items",
            Write("Cross parent", "Task", "Todo", "Medium", null, null, [], null) with { ParentId = otherItem.Id })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsJsonAsync(path + $"/work-items/{item.Id}/dependencies",
            new DependencyWriteDto(otherItem.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync(path + $"/labels/{label.Id}")).StatusCode);
        var latest = await client.GetFromJsonAsync<WorkItemDto>(path + $"/work-items/{item.Id}");
        Assert.Empty(latest!.LabelIds);
        Assert.True(latest.Version > item.Version);
        await fixture.InDb(async db => Assert.Contains(await db.DomainEvents.Where(x => x.ProjectId == project.Id).ToListAsync(),
            x => x.EventType == "WorkItemUpdated" && x.EntityId == item.Id));
        var feed = await client.GetFromJsonAsync<PageDto<ActivityDto>>($"/api/v1/workspaces/{workspace.Id}/activity?projectId={project.Id}&workItemId={item.Id}");
        Assert.Contains(feed!.Items, x => x.Action == "work-item.label-removed" &&
            x.BeforeValues!.Value.GetProperty("LabelId").GetGuid() == label.Id);
    }

    [PostgresFact]
    public async Task Comment_time_and_attachment_validation_and_moderation()
    {
        var owner = await fixture.UserAsync();
        var writer = await fixture.UserAsync();
        var otherWriter = await fixture.UserAsync();
        var viewer = await fixture.UserAsync();
        var workspace = await fixture.WorkspaceAsync(owner, (writer, WorkspaceRole.Developer),
            (otherWriter, WorkspaceRole.Developer), (viewer, WorkspaceRole.Viewer));
        using var ownerClient = fixture.Client(owner);
        using var writerClient = fixture.Client(writer);
        using var otherClient = fixture.Client(otherWriter);
        using var viewerClient = fixture.Client(viewer);
        var project = await ProjectAsync(ownerClient, workspace.Id, "TEAM");
        foreach (var member in new[] { writer, otherWriter, viewer }) await AddProjectMemberAsync(workspace.Id, project.Id, member.Id);
        var path = BasePath(workspace.Id, project.Id);
        var item = await ItemAsync(writerClient, path, "Collaborate", "Task", "Medium", writer.Id, null, []);
        var itemPath = path + $"/work-items/{item.Id}";
        Assert.Equal(HttpStatusCode.Forbidden, (await viewerClient.PostAsJsonAsync(itemPath + "/comments",
            new CommentWriteDto("No", null))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await writerClient.PostAsJsonAsync(itemPath + "/comments",
            new CommentWriteDto($"Hello @{{{Guid.NewGuid()}}}", null))).StatusCode);
        var created = await writerClient.PostAsJsonAsync(itemPath + "/comments", new CommentWriteDto("Initial", null));
        var comment = (await created.Content.ReadFromJsonAsync<CommentDto>())!;
        Assert.Equal(HttpStatusCode.Forbidden, (await otherClient.DeleteAsync(itemPath + $"/comments/{comment.Id}")).StatusCode);
        var moderated = await ownerClient.PutAsJsonAsync(itemPath + $"/comments/{comment.Id}",
            new CommentWriteDto("Reviewed", comment.Version));
        Assert.Equal(HttpStatusCode.OK, moderated.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await writerClient.PostAsJsonAsync(itemPath + "/time-entries",
            new TimeEntryWriteDto(0, null, null))).StatusCode);
        var time = await writerClient.PostAsJsonAsync(itemPath + "/time-entries", new TimeEntryWriteDto(30, null, "work"));
        var entry = (await time.Content.ReadFromJsonAsync<TimeEntryDto>())!;
        Assert.Equal(HttpStatusCode.Forbidden, (await otherClient.DeleteAsync(itemPath + $"/time-entries/{entry.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await ownerClient.DeleteAsync(itemPath + $"/time-entries/{entry.Id}")).StatusCode);
        using var badMultipart = new MultipartFormDataContent();
        var fakePdf = new ByteArrayContent("not a PDF"u8.ToArray());
        fakePdf.Headers.ContentType = new("application/pdf");
        badMultipart.Add(fakePdf, "file", "fake.pdf");
        Assert.Equal(HttpStatusCode.BadRequest, (await writerClient.PostAsync(itemPath + "/attachments", badMultipart)).StatusCode);
        Assert.Equal(0, (await writerClient.GetFromJsonAsync<PageDto<AttachmentDto>>(itemPath + "/attachments"))!.Total);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewerClient.PostAsync(itemPath + "/attachments", null)).StatusCode);
    }

    [PostgresFact]
    public async Task Upload_removes_private_file_if_the_database_commit_fails()
    {
        var owner = await fixture.UserAsync();
        var workspace = await fixture.WorkspaceAsync(owner);
        using var client = fixture.Client(owner);
        var project = await ProjectAsync(client, workspace.Id, "FILES");
        var path = BasePath(workspace.Id, project.Id);
        var item = await ItemAsync(client, path, "Upload", "Task", "Medium", null, null, []);
        await fixture.InDb(async db => await db.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION reject_phase3_attachment() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN
              IF NEW.original_name = 'reject.pdf' THEN RAISE EXCEPTION 'test-only deferred rejection'; END IF;
              RETURN NEW;
            END $$;
            CREATE CONSTRAINT TRIGGER reject_phase3_attachment_trigger
              AFTER INSERT ON attachments DEFERRABLE INITIALLY DEFERRED
              FOR EACH ROW EXECUTE FUNCTION reject_phase3_attachment();
            """));
        try
        {
            var before = Directory.Exists(fixture.AttachmentPath)
                ? Directory.GetFiles(fixture.AttachmentPath).ToHashSet() : [];
            using var multipart = new MultipartFormDataContent();
            var file = new ByteArrayContent("%PDF-1.7 test"u8.ToArray());
            file.Headers.ContentType = new("application/pdf");
            multipart.Add(file, "file", "reject.pdf");
            var response = await client.PostAsync(path + $"/work-items/{item.Id}/attachments", multipart);
            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
            var after = Directory.Exists(fixture.AttachmentPath)
                ? Directory.GetFiles(fixture.AttachmentPath).ToHashSet() : [];
            Assert.True(before.SetEquals(after));
            await fixture.InDb(async db => Assert.False(await db.WorkAttachments.AnyAsync(x =>
                x.WorkItemId == item.Id && x.OriginalName == "reject.pdf")));
        }
        finally
        {
            await fixture.InDb(async db => await db.Database.ExecuteSqlRawAsync("""
                DROP TRIGGER reject_phase3_attachment_trigger ON attachments;
                DROP FUNCTION reject_phase3_attachment();
                """));
        }
    }

    private static string BasePath(Guid workspaceId, Guid projectId) =>
        $"/api/v1/workspaces/{workspaceId}/projects/{projectId}";
    private static WorkItemWriteDto Write(string title, string type, string status, string priority,
        Guid? assignee, DateOnly? dueDate, IReadOnlyList<Guid> labelIds, long? version) =>
        new(title, null, type, status, priority, assignee, null, null, dueDate, null, labelIds, version);
    private static async Task<ProjectResponseDto> ProjectAsync(HttpClient client, Guid workspaceId, string key)
    {
        var response = await client.PostAsJsonAsync($"/api/v1/workspaces/{workspaceId}/projects",
            new { name = key, projectKey = key });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ProjectResponseDto>())!;
    }
    private static async Task<LabelDto> LabelAsync(HttpClient client, string path, string name)
    {
        var response = await client.PostAsJsonAsync(path + "/labels", new LabelWriteDto(name, "#123ABC"));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<LabelDto>())!;
    }
    private static async Task<WorkItemDto> ItemAsync(HttpClient client, string path, string title, string type,
        string priority, Guid? assignee, DateOnly? dueDate, IReadOnlyList<Guid> labelIds)
    {
        var response = await client.PostAsJsonAsync(path + "/work-items", Write(title, type, "Todo", priority,
            assignee, dueDate, labelIds, null));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<WorkItemDto>())!;
    }
    private async Task AddProjectMemberAsync(Guid workspaceId, Guid projectId, Guid userId)
    {
        await fixture.InDb(async db =>
        {
            var membershipId = await db.WorkspaceMemberships.Where(x => x.WorkspaceId == workspaceId && x.UserId == userId)
                .Select(x => x.Id).SingleAsync();
            db.ProjectMemberships.Add(new ProjectMembership { WorkspaceId = workspaceId,
                ProjectId = projectId, WorkspaceMembershipId = membershipId });
            await db.SaveChangesAsync();
        });
    }
}
