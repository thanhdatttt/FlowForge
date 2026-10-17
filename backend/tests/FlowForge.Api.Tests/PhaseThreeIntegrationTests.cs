using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FlowForge.Api.Contracts.Projects;
using FlowForge.Api.Contracts.WorkItems;
using FlowForge.Domain.Projects;
using FlowForge.Domain.Workspaces;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FlowForge.Api.Tests;

public sealed class PhaseThreeIntegrationTests(PhaseTwoFixture fixture) : IClassFixture<PhaseTwoFixture>
{
    [PostgresFact]
    public async Task Work_items_assignments_cycles_archiving_and_permissions()
    {
        var owner = await fixture.UserAsync();
        var developer = await fixture.UserAsync();
        var viewer = await fixture.UserAsync();
        var outsider = await fixture.UserAsync();
        var workspace = await fixture.WorkspaceAsync(owner, (developer, WorkspaceRole.Developer), (viewer, WorkspaceRole.Viewer));
        using var ownerClient = fixture.Client(owner);
        using var developerClient = fixture.Client(developer);
        using var viewerClient = fixture.Client(viewer);
        using var outsiderClient = fixture.Client(outsider);
        var project = await ProjectAsync(ownerClient, workspace.Id);
        var basePath = $"/api/v1/workspaces/{workspace.Id}/projects/{project.Id}";
        await fixture.InDb(async db =>
        {
            var developerMembership = await db.WorkspaceMemberships.SingleAsync(x => x.WorkspaceId == workspace.Id && x.UserId == developer.Id);
            var viewerMembership = await db.WorkspaceMemberships.SingleAsync(x => x.WorkspaceId == workspace.Id && x.UserId == viewer.Id);
            db.ProjectMemberships.Add(new ProjectMembership { WorkspaceId = workspace.Id, ProjectId = project.Id, WorkspaceMembershipId = developerMembership.Id });
            db.ProjectMemberships.Add(new ProjectMembership { WorkspaceId = workspace.Id, ProjectId = project.Id, WorkspaceMembershipId = viewerMembership.Id });
            await db.SaveChangesAsync();
        });
        var first = await ItemAsync(developerClient, basePath, "First", owner.Id);
        var second = await ItemAsync(developerClient, basePath, "Second", developer.Id);
        Assert.Equal("FLOW-1", first.Key);
        Assert.Equal("FLOW-2", second.Key);
        var concurrent = await Task.WhenAll(Enumerable.Range(0, 2).Select(i =>
            developerClient.PostAsJsonAsync(basePath + "/work-items", Write("Concurrent " + i))));
        Assert.All(concurrent, x => Assert.Equal(HttpStatusCode.Created, x.StatusCode));
        var allocated = await Task.WhenAll(concurrent.Select(x => x.Content.ReadFromJsonAsync<WorkItemDto>()));
        Assert.Equal(new[] { "FLOW-3", "FLOW-4" }, allocated.Select(x => x!.Key).Order());
        Assert.Equal(HttpStatusCode.Forbidden, (await viewerClient.PostAsJsonAsync(basePath + "/work-items", Write("Denied"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await outsiderClient.GetAsync(basePath + "/work-items")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await viewerClient.GetAsync(basePath + "/work-items")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await developerClient.PostAsJsonAsync(
            basePath + $"/work-items/{first.Id}/dependencies", new DependencyWriteDto(first.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await developerClient.PostAsJsonAsync(
            basePath + $"/work-items/{first.Id}/dependencies", new DependencyWriteDto(second.Id))).StatusCode);
        var blocked = await developerClient.GetFromJsonAsync<WorkItemDto>(basePath + $"/work-items/{first.Id}");
        Assert.True(blocked!.IsBlocked);
        Assert.True(blocked.Version > first.Version);
        Assert.Equal(HttpStatusCode.BadRequest, (await developerClient.PostAsJsonAsync(
            basePath + $"/work-items/{second.Id}/dependencies", new DependencyWriteDto(first.Id))).StatusCode);
        var updated = Write("First updated") with { ParentId = second.Id, Version = blocked.Version };
        Assert.Equal(HttpStatusCode.OK, (await developerClient.PutAsJsonAsync(basePath + $"/work-items/{first.Id}", updated)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await developerClient.PutAsJsonAsync(basePath + $"/work-items/{first.Id}", updated)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await developerClient.PutAsJsonAsync(basePath + $"/work-items/{second.Id}",
            Write("Second") with { ParentId = first.Id, Version = second.Version })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await ownerClient.PutAsJsonAsync(basePath,
            new { name = project.Name, projectKey = "NEW", description = project.Description,
                leadUserId = (Guid?)null, startsOn = (DateOnly?)null, targetOn = (DateOnly?)null, version = project.Version })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await ownerClient.PostAsync(basePath + $"/work-items/{first.Id}/archive", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await developerClient.PostAsJsonAsync(basePath + $"/work-items/{first.Id}/comments",
            new CommentWriteDto("Blocked", null))).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await ownerClient.DeleteAsync(basePath + $"/members/{developer.Id}")).StatusCode);
        var unassigned = await ownerClient.GetFromJsonAsync<WorkItemDto>(basePath + $"/work-items/{second.Id}");
        Assert.True(unassigned!.NeedsReassignment);
    }

    [PostgresFact]
    public async Task Collaboration_activity_and_file_access_are_project_scoped()
    {
        var owner = await fixture.UserAsync();
        var writer = await fixture.UserAsync();
        var outsider = await fixture.UserAsync();
        var workspace = await fixture.WorkspaceAsync(owner, (writer, WorkspaceRole.Developer), (outsider, WorkspaceRole.Developer));
        using var client = fixture.Client(owner);
        using var writerClient = fixture.Client(writer);
        using var outsiderClient = fixture.Client(outsider);
        var project = await ProjectAsync(client, workspace.Id);
        var basePath = $"/api/v1/workspaces/{workspace.Id}/projects/{project.Id}";
        await fixture.InDb(async db =>
        {
            var member = await db.WorkspaceMemberships.SingleAsync(x => x.WorkspaceId == workspace.Id && x.UserId == writer.Id);
            db.ProjectMemberships.Add(new ProjectMembership { WorkspaceId = workspace.Id, ProjectId = project.Id, WorkspaceMembershipId = member.Id });
            await db.SaveChangesAsync();
        });
        var item = await ItemAsync(writerClient, basePath, "Collaborate", writer.Id);
        var itemPath = basePath + $"/work-items/{item.Id}";
        var mention = $"Hello @{{{owner.Id}}}";
        var commentResponse = await writerClient.PostAsJsonAsync(itemPath + "/comments", new CommentWriteDto(mention, null));
        Assert.Equal(HttpStatusCode.Created, commentResponse.StatusCode);
        var comment = await commentResponse.Content.ReadFromJsonAsync<CommentDto>();
        Assert.Contains(owner.Id, comment!.MentionedUserIds);
        var editedResponse = await writerClient.PutAsJsonAsync(itemPath + $"/comments/{comment.Id}",
            new CommentWriteDto("Revised", comment.Version));
        Assert.Equal(HttpStatusCode.OK, editedResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await writerClient.PutAsJsonAsync(itemPath + $"/comments/{comment.Id}",
            new CommentWriteDto("Stale", comment.Version))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await outsiderClient.GetAsync(itemPath + "/comments")).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await writerClient.PostAsJsonAsync(itemPath + "/time-entries",
            new TimeEntryWriteDto(45, null, "Implementation"))).StatusCode);
        using var multipart = new MultipartFormDataContent();
        var file = new ByteArrayContent("%PDF-1.7 test"u8.ToArray());
        file.Headers.ContentType = new("application/pdf");
        multipart.Add(file, "file", "notes.pdf");
        var upload = await writerClient.PostAsync(itemPath + "/attachments", multipart);
        Assert.Equal(HttpStatusCode.Created, upload.StatusCode);
        var attachment = await upload.Content.ReadFromJsonAsync<AttachmentDto>();
        string? storedKey = null;
        await fixture.InDb(async db => storedKey = await db.WorkAttachments.Where(x => x.Id == attachment!.Id)
            .Select(x => x.StorageKey).SingleAsync());
        var storedPath = Path.Combine(fixture.AttachmentPath, storedKey!);
        Assert.True(File.Exists(storedPath));
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(itemPath + $"/attachments/{attachment!.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await outsiderClient.GetAsync(itemPath + $"/attachments/{attachment.Id}")).StatusCode);
        var activity = await writerClient.GetFromJsonAsync<PageDto<ActivityDto>>($"/api/v1/workspaces/{workspace.Id}/activity?projectId={project.Id}");
        Assert.Contains(activity!.Items, x => x.Action == "work-item.created");
        Assert.Contains(activity.Items, x => x.Action == "comment.created");
        Assert.Equal(HttpStatusCode.NoContent, (await writerClient.DeleteAsync(itemPath + $"/attachments/{attachment.Id}")).StatusCode);
        Assert.False(File.Exists(storedPath));
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(itemPath + $"/attachments/{attachment.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await writerClient.DeleteAsync(itemPath + $"/comments/{comment.Id}")).StatusCode);
        Assert.Equal(0, (await writerClient.GetFromJsonAsync<PageDto<CommentDto>>(itemPath + "/comments"))!.Total);
    }

    private static WorkItemWriteDto Write(string title) => new(title, null, "Task", "Todo", "Medium", null,
        null, null, null, null, [], null);
    private static async Task<ProjectResponseDto> ProjectAsync(HttpClient client, Guid workspaceId)
    {
        var response = await client.PostAsJsonAsync($"/api/v1/workspaces/{workspaceId}/projects",
            new { name = "Phase 3", projectKey = "FLOW", description = "work", leadUserId = (Guid?)null,
                startsOn = (DateOnly?)null, targetOn = (DateOnly?)null });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ProjectResponseDto>())!;
    }
    private static async Task<WorkItemDto> ItemAsync(HttpClient client, string basePath, string title, Guid assignee)
    {
        var response = await client.PostAsJsonAsync(basePath + "/work-items", Write(title) with { AssigneeUserId = assignee });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<WorkItemDto>())!;
    }
}
